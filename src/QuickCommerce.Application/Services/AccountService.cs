using System.Security.Cryptography;
using System.Text;
using FluentValidation;
using FluentValidation.Results;
using QuickCommerce.Application.DTOs;
using QuickCommerce.Application.Interfaces;
using QuickCommerce.Application.Validators;

namespace QuickCommerce.Application.Services;

public sealed class AccountService(
    IAccountStore store,
    IPasswordHashing hashing,
    IAuthenticationService authentication,
    IEmailSender email,
    ICurrentUser currentUser,
    IValidator<RegisterCustomerRequest> registerValidator,
    IValidator<ForgotPasswordRequest> forgotValidator,
    IValidator<ResetPasswordRequest> resetValidator,
    IValidator<ChangePasswordRequest> changeValidator,
    IValidator<UpdateCustomerProfileRequest> profileValidator,
    AccountSettings settings,
    RegistrationSettings registration,
    TimeProvider clock) : IAccountService
{
    // One message for every reset failure (unknown account, wrong, expired, used or exhausted code) so callers learn nothing.
    private const string InvalidCode = "Invalid or expired code.";
    private const string EmailTaken = "An account with this email already exists.";

    private DateTime Now => clock.GetUtcNow().UtcDateTime;

    public async Task<AccountResult<AuthenticationResponse>> RegisterAsync(RegisterCustomerRequest request, string? ipAddress, CancellationToken cancellationToken = default)
    {
        var validation = await registerValidator.ValidateAsync(request, cancellationToken);
        if (!validation.IsValid)
        {
            return Invalid<AuthenticationResponse>(validation);
        }

        var organization = await store.ResolveRegistrationOrganizationAsync(registration.OrganizationId, cancellationToken);
        if (organization.OrganizationId is not { } organizationId)
        {
            return AccountResult<AuthenticationResponse>.Misconfigured(organization.Error ?? "Registration is not configured.");
        }

        var normalizedEmail = AccountRules.NormalizeEmail(request.Email);
        if (await store.EmailExistsAsync(normalizedEmail, cancellationToken))
        {
            return AccountResult<AuthenticationResponse>.Conflict(EmailTaken);
        }

        var (first, last) = AccountRules.SplitName(request.FullName);
        var created = await store.TryCreateCustomerAsync(
            new NewCustomerAccount(organizationId, request.FullName.Trim(), first, last, normalizedEmail, AccountRules.NormalizePhone(request.PhoneNumber), hashing.Hash(request.Password)),
            cancellationToken);
        if (!created)
        {
            return AccountResult<AuthenticationResponse>.Conflict(EmailTaken);
        }

        // Signing in reuses the normal login path, so tokens, roles and permissions are issued exactly as for any other login.
        var login = await authentication.LoginAsync(new LoginRequest(normalizedEmail, request.Password), ipAddress, cancellationToken);
        return login.Succeeded && login.Response is not null
            ? AccountResult<AuthenticationResponse>.Ok(login.Response)
            : AccountResult<AuthenticationResponse>.Unauthorized("Your account was created, but signing in failed. Please log in.");
    }

    public async Task<AccountResult<bool>> RequestPasswordResetAsync(ForgotPasswordRequest request, string? language, string? ipAddress, CancellationToken cancellationToken = default)
    {
        var validation = await forgotValidator.ValidateAsync(request, cancellationToken);
        if (!validation.IsValid)
        {
            return Invalid<bool>(validation);
        }

        var normalizedEmail = AccountRules.NormalizeEmail(request.Email);
        var user = await store.FindActiveCustomerByEmailAsync(normalizedEmail, cancellationToken);
        if (user is null)
        {
            return AccountResult<bool>.Ok(true); // same answer whether or not the account exists
        }

        var now = Now;
        if (await store.CountResetCodesSinceAsync(user.UserId, now.AddHours(-1), cancellationToken) >= settings.MaxCodeRequestsPerHour)
        {
            return AccountResult<bool>.Ok(true); // over the hourly cap: say nothing, send nothing
        }

        var code = ResetCodes.Generate();
        await store.CreateResetCodeAsync(
            user.UserId,
            ResetCodes.Hash(settings.ResetCodeKey, user.UserId, code),
            now,
            now.AddMinutes(settings.CodeLifetimeMinutes),
            ipAddress,
            cancellationToken);

        try
        {
            await email.SendAsync(AccountEmails.PasswordReset(user.Email, ResetCodes.Format(code), settings.CodeLifetimeMinutes, language), cancellationToken);
        }
        catch (Exception)
        {
            // A mail outage must not change the answer (that would reveal the account exists). The sender logs the failure.
        }

        return AccountResult<bool>.Ok(true);
    }

    public async Task<AccountResult<bool>> ResetPasswordAsync(ResetPasswordRequest request, string? ipAddress, CancellationToken cancellationToken = default)
    {
        var validation = await resetValidator.ValidateAsync(request, cancellationToken);
        if (!validation.IsValid)
        {
            return Invalid<bool>(validation);
        }

        var user = await store.FindActiveCustomerByEmailAsync(AccountRules.NormalizeEmail(request.Email), cancellationToken);
        if (user is null)
        {
            return AccountResult<bool>.Invalid(InvalidCode);
        }

        var now = Now;
        var open = await store.GetOpenResetCodeAsync(user.UserId, now, settings.MaxCodeAttempts, cancellationToken);
        if (open is null)
        {
            return AccountResult<bool>.Invalid(InvalidCode);
        }

        var submitted = ResetCodes.Hash(settings.ResetCodeKey, user.UserId, ResetCodes.Normalize(request.Code));
        if (!ResetCodes.Matches(open.CodeHash, submitted))
        {
            await store.RecordFailedResetAttemptAsync(open.Id, cancellationToken);
            return AccountResult<bool>.Invalid(InvalidCode);
        }

        // Two simultaneous submissions of the right code: only one wins.
        if (!await store.TryConsumeResetCodeAsync(open.Id, now, cancellationToken))
        {
            return AccountResult<bool>.Invalid(InvalidCode);
        }

        await store.SetPasswordAsync(user.UserId, hashing.Hash(request.NewPassword), now, cancellationToken);
        await store.RevokeSessionsAsync(user.UserId, null, ipAddress, now, cancellationToken);
        return AccountResult<bool>.Ok(true);
    }

    public async Task<AccountResult<bool>> ChangePasswordAsync(ChangePasswordRequest request, string? ipAddress, CancellationToken cancellationToken = default)
    {
        if (!TryGetUserId(out var userId))
        {
            return AccountResult<bool>.Unauthorized("Authentication is required.");
        }

        var validation = await changeValidator.ValidateAsync(request, cancellationToken);
        if (!validation.IsValid)
        {
            return Invalid<bool>(validation);
        }

        if (await store.FindActiveCustomerByIdAsync(userId, cancellationToken) is null)
        {
            return AccountResult<bool>.NotFound("Account not found.");
        }

        var currentHash = await store.GetPasswordHashAsync(userId, cancellationToken);
        if (currentHash is null || !hashing.Verify(currentHash, request.CurrentPassword))
        {
            return AccountResult<bool>.Invalid("Your current password is incorrect.", ["Your current password is incorrect."]);
        }

        var now = Now;
        await store.SetPasswordAsync(userId, hashing.Hash(request.NewPassword), now, cancellationToken);
        await store.RevokeSessionsAsync(userId, string.IsNullOrEmpty(request.RefreshToken) ? null : HashToken(request.RefreshToken), ipAddress, now, cancellationToken);
        return AccountResult<bool>.Ok(true);
    }

    public async Task<AccountResult<CustomerProfileResponse>> GetProfileAsync(CancellationToken cancellationToken = default)
    {
        if (!TryGetUserId(out var userId))
        {
            return AccountResult<CustomerProfileResponse>.Unauthorized("Authentication is required.");
        }

        return await store.GetProfileAsync(userId, cancellationToken) is { } profile
            ? AccountResult<CustomerProfileResponse>.Ok(profile)
            : AccountResult<CustomerProfileResponse>.NotFound("Customer profile not found.");
    }

    public async Task<AccountResult<CustomerProfileResponse>> UpdateProfileAsync(UpdateCustomerProfileRequest request, CancellationToken cancellationToken = default)
    {
        if (!TryGetUserId(out var userId))
        {
            return AccountResult<CustomerProfileResponse>.Unauthorized("Authentication is required.");
        }

        var validation = await profileValidator.ValidateAsync(request, cancellationToken);
        if (!validation.IsValid)
        {
            return Invalid<CustomerProfileResponse>(validation);
        }

        if (await store.GetProfileAsync(userId, cancellationToken) is null)
        {
            return AccountResult<CustomerProfileResponse>.NotFound("Customer profile not found.");
        }

        var (first, last) = AccountRules.SplitName(request.FullName);
        await store.UpdateProfileAsync(userId, request.FullName.Trim(), first, last, AccountRules.NormalizePhone(request.PhoneNumber), cancellationToken);
        return AccountResult<CustomerProfileResponse>.Ok((await store.GetProfileAsync(userId, cancellationToken))!);
    }

    private bool TryGetUserId(out Guid userId)
    {
        userId = Guid.Empty;
        return currentUser.IsAuthenticated && Guid.TryParse(currentUser.UserId, out userId);
    }

    private static AccountResult<T> Invalid<T>(ValidationResult validation)
    {
        var errors = validation.Errors.Select(error => error.ErrorMessage).Distinct().ToArray();
        return AccountResult<T>.Invalid(string.Join(" ", errors), errors);
    }

    // Same hashing as the refresh-token table (SHA-256, upper-case hex) so the caller's own session can be kept.
    private static string HashToken(string token) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(token)));
}
