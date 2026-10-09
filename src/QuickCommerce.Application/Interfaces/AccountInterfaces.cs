using QuickCommerce.Application.DTOs;
using QuickCommerce.Application.Services;

namespace QuickCommerce.Application.Interfaces;

/// <summary>Customer self-service accounts: register, password reset, password change, profile.</summary>
public interface IAccountService
{
    /// <summary>Creates the customer in the configured organization and signs them in.</summary>
    Task<AccountResult<AuthenticationResponse>> RegisterAsync(RegisterCustomerRequest request, string? ipAddress, CancellationToken cancellationToken = default);

    /// <summary>Emails a reset code when an active customer account exists. Always succeeds for a well-formed email, so it never reveals whether the account exists.</summary>
    Task<AccountResult<bool>> RequestPasswordResetAsync(ForgotPasswordRequest request, string? language, string? ipAddress, CancellationToken cancellationToken = default);

    /// <summary>Sets a new password from a valid code and signs out every session. Every failure looks the same to the caller.</summary>
    Task<AccountResult<bool>> ResetPasswordAsync(ResetPasswordRequest request, string? ipAddress, CancellationToken cancellationToken = default);

    Task<AccountResult<bool>> ChangePasswordAsync(ChangePasswordRequest request, string? ipAddress, CancellationToken cancellationToken = default);
    Task<AccountResult<CustomerProfileResponse>> GetProfileAsync(CancellationToken cancellationToken = default);
    Task<AccountResult<CustomerProfileResponse>> UpdateProfileAsync(UpdateCustomerProfileRequest request, CancellationToken cancellationToken = default);
}

/// <summary>Sends account emails. Implementations must never log message bodies in production.</summary>
public interface IEmailSender
{
    Task SendAsync(EmailMessage message, CancellationToken cancellationToken = default);
}

/// <summary>Password hashing, kept behind an interface so Application does not depend on ASP.NET Core Identity.</summary>
public interface IPasswordHashing
{
    string Hash(string password);
    bool Verify(string hash, string password);
}

public sealed record OrganizationChoice(Guid? OrganizationId, string? Error);

public sealed record NewCustomerAccount(Guid OrganizationId, string FullName, string FirstName, string LastName, string Email, string? PhoneNumber, string PasswordHash);

public sealed record AccountUserInfo(Guid UserId, string Email, string DisplayName);

public sealed record ResetCodeInfo(Guid Id, string CodeHash, int FailedAttempts);

/// <summary>Persistence for the account flows. Kept narrow so the security rules can be tested without a database.</summary>
public interface IAccountStore
{
    Task<OrganizationChoice> ResolveRegistrationOrganizationAsync(Guid? configuredOrganizationId, CancellationToken cancellationToken = default);
    Task<bool> EmailExistsAsync(string normalizedEmail, CancellationToken cancellationToken = default);

    /// <summary>Creates user, customer, credential and role in one save. Returns false when the email was taken meanwhile (unique index).</summary>
    Task<bool> TryCreateCustomerAsync(NewCustomerAccount account, CancellationToken cancellationToken = default);

    Task<AccountUserInfo?> FindActiveCustomerByEmailAsync(string normalizedEmail, CancellationToken cancellationToken = default);
    Task<AccountUserInfo?> FindActiveCustomerByIdAsync(Guid userId, CancellationToken cancellationToken = default);

    Task<int> CountResetCodesSinceAsync(Guid userId, DateTime sinceUtc, CancellationToken cancellationToken = default);

    /// <summary>Invalidates the user's open codes and stores the new one.</summary>
    Task CreateResetCodeAsync(Guid userId, string codeHash, DateTime createdUtc, DateTime expiresUtc, string? ipAddress, CancellationToken cancellationToken = default);

    Task<ResetCodeInfo?> GetOpenResetCodeAsync(Guid userId, DateTime nowUtc, int maxAttempts, CancellationToken cancellationToken = default);
    Task RecordFailedResetAttemptAsync(Guid codeId, CancellationToken cancellationToken = default);

    /// <summary>Atomically marks the code used. False when someone else already used it.</summary>
    Task<bool> TryConsumeResetCodeAsync(Guid codeId, DateTime nowUtc, CancellationToken cancellationToken = default);

    Task<string?> GetPasswordHashAsync(Guid userId, CancellationToken cancellationToken = default);

    /// <summary>Sets the password hash (creating the credential if missing), clears failed-login counters and any lockout.</summary>
    Task SetPasswordAsync(Guid userId, string passwordHash, DateTime nowUtc, CancellationToken cancellationToken = default);

    /// <summary>Revokes every refresh token of the user, except the one with <paramref name="exceptRefreshTokenHash"/>.</summary>
    Task RevokeSessionsAsync(Guid userId, string? exceptRefreshTokenHash, string? ipAddress, DateTime nowUtc, CancellationToken cancellationToken = default);

    Task<CustomerProfileResponse?> GetProfileAsync(Guid userId, CancellationToken cancellationToken = default);
    Task UpdateProfileAsync(Guid userId, string fullName, string firstName, string lastName, string? phoneNumber, CancellationToken cancellationToken = default);
}
