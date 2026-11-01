using QuickCommerce.Application.DTOs;

namespace QuickCommerce.Application.Interfaces;

/// <param name="Phone">A mobile number, with or without +91.</param>
public sealed record OtpRequest(string Phone);

/// <param name="FullName">Only used when the number is new and an account is made.</param>
public sealed record OtpVerifyRequest(string Phone, string Code, string? FullName = null);

/// <summary>What the app shows after a code was sent: when it may ask again, and until when the code works.</summary>
public sealed record OtpSentResponse(int ResendAfterSeconds, int ExpiresInSeconds);

public sealed record GoogleSignInRequest(string IdToken);

/// <summary>A person Google vouches for. Only a token that passed every check becomes one of these.</summary>
public sealed record GoogleIdentity(string Subject, string Email, bool EmailVerified, string? Name);

public enum SignInStatus
{
    Succeeded,
    Invalid,

    /// <summary>Wrong, expired, used or exhausted code. One answer for all, so callers learn nothing.</summary>
    Unauthorized,
    TooManyRequests,
    Conflict,
    Forbidden,
    NotConfigured,
    Unavailable
}

public sealed record SignInResult<T>(SignInStatus Status, T? Value = default, string? Message = null, int? RetryAfterSeconds = null)
{
    public static SignInResult<T> Ok(T value) => new(SignInStatus.Succeeded, value);
    public static SignInResult<T> Fail(SignInStatus status, string message, int? retryAfterSeconds = null) => new(status, default, message, retryAfterSeconds);
}

public interface ISignInService
{
    /// <summary>Texts a code to the number. The answer is the same whether or not the number has an account.</summary>
    Task<SignInResult<OtpSentResponse>> RequestOtpAsync(OtpRequest request, string? ipAddress, CancellationToken cancellationToken = default);

    /// <summary>Signs in with the code: to the account whose verified number this is, or to a new account.</summary>
    Task<SignInResult<AuthenticationResponse>> VerifyOtpAsync(OtpVerifyRequest request, string? ipAddress, CancellationToken cancellationToken = default);

    /// <summary>For a signed-in customer: texts a code to a number they want to verify for their own account.</summary>
    Task<SignInResult<OtpSentResponse>> RequestPhoneVerificationAsync(OtpRequest request, string? ipAddress, CancellationToken cancellationToken = default);

    /// <summary>Marks the number verified for the signed-in customer's account, so they can use it to sign in.</summary>
    Task<SignInResult<bool>> VerifyPhoneAsync(OtpVerifyRequest request, string? ipAddress, CancellationToken cancellationToken = default);

    Task<SignInResult<AuthenticationResponse>> GoogleSignInAsync(GoogleSignInRequest request, string? ipAddress, CancellationToken cancellationToken = default);
}

/// <summary>Sends the code by text message. Implementations must never log the code outside Development.</summary>
public interface IOtpSender
{
    Task SendAsync(string phone, string code, CancellationToken cancellationToken = default);
}

/// <summary>Checks a Google ID token (signature, expiry, issuer and that it was made for this app). Null = not a valid token.</summary>
public interface IGoogleTokenValidator
{
    bool IsConfigured { get; }
    Task<GoogleIdentity?> ValidateAsync(string idToken, CancellationToken cancellationToken = default);
}

/// <summary>Starts a normal session (tokens, roles, permissions) for someone whose identity was already proven another way.</summary>
public interface ISessionIssuer
{
    Task<AuthenticationResult> IssueAsync(Guid userId, string? ipAddress, CancellationToken cancellationToken = default);
}

public sealed record OtpRecord(Guid Id, string CodeHash, int FailedAttempts, DateTime CreatedAt);

public sealed record CustomerIdentity(Guid UserId, bool EmailVerified, bool HasPassword);

/// <summary>Persistence for text-message codes, verified numbers and Google links. Kept narrow so the rules can be tested without a database.</summary>
public interface IIdentityStore
{
    Task<int> CountOtpRequestsSinceAsync(string phone, DateTime sinceUtc, CancellationToken cancellationToken = default);
    Task<OtpRecord?> GetOpenOtpAsync(string phone, string purpose, Guid? userId, DateTime nowUtc, int maxAttempts, CancellationToken cancellationToken = default);

    /// <summary>Closes the open codes for this number and purpose and stores the new one.</summary>
    Task CreateOtpAsync(string phone, string purpose, Guid? userId, string codeHash, DateTime createdUtc, DateTime expiresUtc, string? ipAddress, CancellationToken cancellationToken = default);

    Task RecordFailedOtpAttemptAsync(Guid id, CancellationToken cancellationToken = default);

    /// <summary>Atomically marks the code used. False when someone else already used it.</summary>
    Task<bool> TryConsumeOtpAsync(Guid id, DateTime nowUtc, CancellationToken cancellationToken = default);

    Task<Guid?> FindActiveCustomerByVerifiedPhoneAsync(string phone, CancellationToken cancellationToken = default);

    /// <summary>Makes a customer with a verified number and no password. If someone made it first, returns that account.</summary>
    Task<Guid?> CreatePhoneCustomerAsync(Guid organizationId, string phone, string fullName, DateTime nowUtc, CancellationToken cancellationToken = default);

    /// <summary>Stores the number as verified. False when another account already holds it verified.</summary>
    Task<bool> TryMarkPhoneVerifiedAsync(Guid userId, string phone, DateTime nowUtc, CancellationToken cancellationToken = default);

    Task<CustomerIdentity?> FindActiveCustomerByGoogleAsync(string subject, CancellationToken cancellationToken = default);
    Task<CustomerIdentity?> FindActiveCustomerByEmailAsync(string normalizedEmail, CancellationToken cancellationToken = default);
    Task<Guid?> CreateGoogleCustomerAsync(Guid organizationId, string normalizedEmail, string fullName, string subject, DateTime nowUtc, CancellationToken cancellationToken = default);

    /// <summary>Links the Google id and marks the email verified. With <paramref name="wipeSignIn"/> the old password and every session are removed.</summary>
    Task LinkGoogleAsync(Guid userId, string subject, bool wipeSignIn, string? ipAddress, DateTime nowUtc, CancellationToken cancellationToken = default);
}
