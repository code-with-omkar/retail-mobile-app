using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Logging;
using QuickCommerce.Application.DTOs;
using QuickCommerce.Application.Interfaces;
using QuickCommerce.Application.Validators;
using QuickCommerce.Domain;

namespace QuickCommerce.Application.Services;

/// <summary>Configuration section "Otp": how text-message codes behave.</summary>
public sealed class OtpSettings
{
    public const string SectionName = "Otp";

    /// <summary>"Log" writes the code to the API log (Development only) and "Msg91" sends a real text message.</summary>
    public string Sender { get; set; } = "Log";

    /// <summary>Server secret for hashing codes. When empty, the account reset-code secret is used.</summary>
    public string CodeKey { get; set; } = "";

    public int LifetimeMinutes { get; set; } = 5;
    public int MaxAttempts { get; set; } = 5;
    public int ResendSeconds { get; set; } = 30;
    public int MaxRequestsPerHour { get; set; } = 5;

    public void Validate()
    {
        if (LifetimeMinutes is < 1 or > 30 || MaxAttempts is < 1 or > 10 || ResendSeconds is < 0 or > 600 || MaxRequestsPerHour is < 1 or > 30)
        {
            throw new InvalidOperationException("Otp settings are out of range (LifetimeMinutes 1-30, MaxAttempts 1-10, ResendSeconds 0-600, MaxRequestsPerHour 1-30).");
        }
    }
}

/// <summary>Configuration section "Google": the OAuth client ids our app's ID tokens are made for. Empty = Google sign-in is off.</summary>
public sealed class GoogleSettings
{
    public const string SectionName = "Google";
    public string[] ClientIds { get; set; } = [];
}

public static partial class Phones
{
    [GeneratedRegex("^[6-9][0-9]{9}$")]
    private static partial Regex IndianMobile();

    /// <summary>An Indian mobile number as +91XXXXXXXXXX, or null when it is not one. Accepts spaces, dashes, "+91", "91" and a leading 0.</summary>
    public static string? ToE164(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
        {
            return null;
        }

        var digits = new string(raw.Where(char.IsAsciiDigit).ToArray());
        if (raw.Trim().StartsWith('+') && !digits.StartsWith("91", StringComparison.Ordinal))
        {
            return null;
        }

        if (digits.Length == 12 && digits.StartsWith("91", StringComparison.Ordinal))
        {
            digits = digits[2..];
        }
        else if (digits.Length == 11 && digits[0] == '0')
        {
            digits = digits[1..];
        }

        return IndianMobile().IsMatch(digits) ? "+91" + digits : null;
    }
}

public sealed partial class SignInService(
    IIdentityStore store,
    IAccountStore accounts,
    IOtpSender sender,
    IGoogleTokenValidator google,
    ISessionIssuer sessions,
    ICurrentUserContextResolver contextResolver,
    OtpSettings otp,
    AccountSettings account,
    RegistrationSettings registration,
    TimeProvider clock,
    ILogger<SignInService> log) : ISignInService
{
    private const string SignIn = "SignIn";
    private const string VerifyPhone = "VerifyPhone";
    private const string InvalidCode = "Invalid or expired code.";
    private const string NotPossible = "Sign-in is not possible with these details.";

    [GeneratedRegex("^[0-9]{6}$")]
    private static partial Regex SixDigits();

    private DateTime Now => clock.GetUtcNow().UtcDateTime;

    private string Key => string.IsNullOrWhiteSpace(otp.CodeKey) ? account.ResetCodeKey : otp.CodeKey;

    private string Hash(string purpose, string phone, Guid? userId, string code)
    {
        using var hmac = new HMACSHA256(Encoding.UTF8.GetBytes(Key));
        return Convert.ToHexString(hmac.ComputeHash(Encoding.UTF8.GetBytes($"otp|{purpose}|{phone}|{userId:N}|{code}")));
    }

    public Task<SignInResult<OtpSentResponse>> RequestOtpAsync(OtpRequest request, string? ipAddress, CancellationToken cancellationToken = default) =>
        SendCodeAsync(request, SignIn, null, ipAddress, cancellationToken);

    public async Task<SignInResult<OtpSentResponse>> RequestPhoneVerificationAsync(OtpRequest request, string? ipAddress, CancellationToken cancellationToken = default)
    {
        var userId = await CurrentCustomerAsync(cancellationToken);
        return userId is null
            ? SignInResult<OtpSentResponse>.Fail(SignInStatus.Forbidden, "Sign in to verify your number.")
            : await SendCodeAsync(request, VerifyPhone, userId, ipAddress, cancellationToken);
    }

    private async Task<Guid?> CurrentCustomerAsync(CancellationToken cancellationToken)
    {
        var context = await contextResolver.ResolveAsync(cancellationToken);
        return context is { Role: Role.Customer } ? context.UserId : null;
    }

    private async Task<SignInResult<OtpSentResponse>> SendCodeAsync(OtpRequest request, string purpose, Guid? userId, string? ipAddress, CancellationToken cancellationToken)
    {
        var phone = Phones.ToE164(request.Phone);
        if (phone is null)
        {
            return SignInResult<OtpSentResponse>.Fail(SignInStatus.Invalid, "Enter a 10-digit mobile number.");
        }

        var now = Now;
        if (await store.CountOtpRequestsSinceAsync(phone, now.AddHours(-1), cancellationToken) >= otp.MaxRequestsPerHour)
        {
            return SignInResult<OtpSentResponse>.Fail(SignInStatus.TooManyRequests, "Too many codes were asked for this number. Try again in a while.", 3600);
        }

        var open = await store.GetOpenOtpAsync(phone, purpose, userId, now, otp.MaxAttempts, cancellationToken);
        if (open is not null && open.CreatedAt.AddSeconds(otp.ResendSeconds) > now)
        {
            var wait = (int)Math.Ceiling((open.CreatedAt.AddSeconds(otp.ResendSeconds) - now).TotalSeconds);
            return SignInResult<OtpSentResponse>.Fail(SignInStatus.TooManyRequests, $"Wait {wait} seconds before asking for another code.", wait);
        }

        var code = RandomNumberGenerator.GetInt32(0, 1_000_000).ToString("D6");
        await store.CreateOtpAsync(phone, purpose, userId, Hash(purpose, phone, userId, code), now, now.AddMinutes(otp.LifetimeMinutes), ipAddress, cancellationToken);
        try
        {
            await sender.SendAsync(phone, code, cancellationToken);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            // The number is not logged; the sender's own error says what went wrong.
            log.LogError(exception, "A sign-in code could not be sent");
            return SignInResult<OtpSentResponse>.Fail(SignInStatus.Unavailable, "We could not send the code. Please try again.");
        }

        return SignInResult<OtpSentResponse>.Ok(new OtpSentResponse(otp.ResendSeconds, otp.LifetimeMinutes * 60));
    }

    /// <summary>Checks the code. True = it was right and is now used up. Every failure is the same to the caller.</summary>
    private async Task<bool> CheckCodeAsync(string phone, string? code, string purpose, Guid? userId, CancellationToken cancellationToken)
    {
        var open = await store.GetOpenOtpAsync(phone, purpose, userId, Now, otp.MaxAttempts, cancellationToken);
        var given = (code ?? string.Empty).Trim();
        if (open is null || !SixDigits().IsMatch(given))
        {
            return false;
        }

        if (!ResetCodes.Matches(open.CodeHash, Hash(purpose, phone, userId, given)))
        {
            await store.RecordFailedOtpAttemptAsync(open.Id, cancellationToken);
            return false;
        }

        return await store.TryConsumeOtpAsync(open.Id, Now, cancellationToken);
    }

    public async Task<SignInResult<AuthenticationResponse>> VerifyOtpAsync(OtpVerifyRequest request, string? ipAddress, CancellationToken cancellationToken = default)
    {
        var phone = Phones.ToE164(request.Phone);
        if (phone is null || !await CheckCodeAsync(phone, request.Code, SignIn, null, cancellationToken))
        {
            return SignInResult<AuthenticationResponse>.Fail(SignInStatus.Unauthorized, InvalidCode);
        }

        var userId = await store.FindActiveCustomerByVerifiedPhoneAsync(phone, cancellationToken);
        if (userId is null)
        {
            var organization = await accounts.ResolveRegistrationOrganizationAsync(registration.OrganizationId, cancellationToken);
            if (organization.OrganizationId is not { } organizationId)
            {
                return SignInResult<AuthenticationResponse>.Fail(SignInStatus.NotConfigured, organization.Error ?? "Sign-up is not configured.");
            }

            var name = string.IsNullOrWhiteSpace(request.FullName) ? "Customer" : request.FullName.Trim();
            userId = await store.CreatePhoneCustomerAsync(organizationId, phone, name.Length > 120 ? name[..120] : name, Now, cancellationToken);
        }

        return await StartSessionAsync(userId, ipAddress, cancellationToken);
    }

    public async Task<SignInResult<bool>> VerifyPhoneAsync(OtpVerifyRequest request, string? ipAddress, CancellationToken cancellationToken = default)
    {
        var userId = await CurrentCustomerAsync(cancellationToken);
        if (userId is null)
        {
            return SignInResult<bool>.Fail(SignInStatus.Forbidden, "Sign in to verify your number.");
        }

        var phone = Phones.ToE164(request.Phone);
        if (phone is null || !await CheckCodeAsync(phone, request.Code, VerifyPhone, userId, cancellationToken))
        {
            return SignInResult<bool>.Fail(SignInStatus.Unauthorized, InvalidCode);
        }

        return await store.TryMarkPhoneVerifiedAsync(userId.Value, phone, Now, cancellationToken)
            ? SignInResult<bool>.Ok(true)
            : SignInResult<bool>.Fail(SignInStatus.Conflict, "This number cannot be verified for your account.");
    }

    public async Task<SignInResult<AuthenticationResponse>> GoogleSignInAsync(GoogleSignInRequest request, string? ipAddress, CancellationToken cancellationToken = default)
    {
        if (!google.IsConfigured)
        {
            return SignInResult<AuthenticationResponse>.Fail(SignInStatus.NotConfigured, "Google sign-in is not available right now.");
        }

        var identity = string.IsNullOrWhiteSpace(request.IdToken) ? null : await google.ValidateAsync(request.IdToken, cancellationToken);
        if (identity is null || !identity.EmailVerified || string.IsNullOrWhiteSpace(identity.Email))
        {
            return SignInResult<AuthenticationResponse>.Fail(SignInStatus.Unauthorized, "Google sign-in failed.");
        }

        var email = AccountRules.NormalizeEmail(identity.Email);
        var known = await store.FindActiveCustomerByGoogleAsync(identity.Subject, cancellationToken);
        if (known is not null)
        {
            return await StartSessionAsync(known.UserId, ipAddress, cancellationToken);
        }

        var sameEmail = await store.FindActiveCustomerByEmailAsync(email, cancellationToken);
        if (sameEmail is not null)
        {
            // Google proves who owns this email. If nobody ever proved it for this account, whoever made it may be someone else
            // who typed in this email first, so its password and every open session are removed before the real owner gets in.
            await store.LinkGoogleAsync(sameEmail.UserId, identity.Subject, wipeSignIn: !sameEmail.EmailVerified, ipAddress, Now, cancellationToken);
            return await StartSessionAsync(sameEmail.UserId, ipAddress, cancellationToken);
        }

        var organization = await accounts.ResolveRegistrationOrganizationAsync(registration.OrganizationId, cancellationToken);
        if (organization.OrganizationId is not { } organizationId)
        {
            return SignInResult<AuthenticationResponse>.Fail(SignInStatus.NotConfigured, organization.Error ?? "Sign-up is not configured.");
        }

        var name = string.IsNullOrWhiteSpace(identity.Name) ? email.Split('@')[0] : identity.Name.Trim();
        var created = await store.CreateGoogleCustomerAsync(organizationId, email, name.Length > 120 ? name[..120] : name, identity.Subject, Now, cancellationToken);
        return await StartSessionAsync(created, ipAddress, cancellationToken);
    }

    private async Task<SignInResult<AuthenticationResponse>> StartSessionAsync(Guid? userId, string? ipAddress, CancellationToken cancellationToken)
    {
        if (userId is null)
        {
            return SignInResult<AuthenticationResponse>.Fail(SignInStatus.Forbidden, NotPossible);
        }

        var session = await sessions.IssueAsync(userId.Value, ipAddress, cancellationToken);
        return session.Succeeded && session.Response is not null
            ? SignInResult<AuthenticationResponse>.Ok(session.Response)
            : SignInResult<AuthenticationResponse>.Fail(SignInStatus.Forbidden, NotPossible);
    }
}
