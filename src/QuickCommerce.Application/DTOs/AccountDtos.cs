namespace QuickCommerce.Application.DTOs;

public sealed record RegisterCustomerRequest(string FullName, string Email, string Password, string? PhoneNumber);

public sealed record ForgotPasswordRequest(string Email);

/// <param name="Code">The emailed one-time code, with or without the dash (ABCD-EFGH).</param>
public sealed record ResetPasswordRequest(string Email, string Code, string NewPassword);

/// <param name="RefreshToken">The caller's current refresh token. It is kept; every other session is signed out.</param>
public sealed record ChangePasswordRequest(string CurrentPassword, string NewPassword, string? RefreshToken = null);

public sealed record UpdateCustomerProfileRequest(string FullName, string? PhoneNumber);

public sealed record CustomerProfileResponse(Guid Id, string FullName, string Email, string? PhoneNumber);

public enum AccountStatus
{
    Succeeded,
    InvalidRequest,
    Conflict,
    NotFound,
    Unauthorized,
    ConfigurationError
}

/// <summary>Outcome of an account operation. <see cref="Errors"/> lists field or rule messages for <see cref="AccountStatus.InvalidRequest"/>.</summary>
public sealed record AccountResult<T>(AccountStatus Status, T? Value = default, string? Message = null, IReadOnlyList<string>? Errors = null)
{
    public bool Succeeded => Status == AccountStatus.Succeeded;

    public static AccountResult<T> Ok(T value) => new(AccountStatus.Succeeded, value);
    public static AccountResult<T> Invalid(string message, IReadOnlyList<string>? errors = null) => new(AccountStatus.InvalidRequest, default, message, errors ?? [message]);
    public static AccountResult<T> Conflict(string message) => new(AccountStatus.Conflict, default, message);
    public static AccountResult<T> NotFound(string message) => new(AccountStatus.NotFound, default, message);
    public static AccountResult<T> Unauthorized(string message) => new(AccountStatus.Unauthorized, default, message);
    public static AccountResult<T> Misconfigured(string message) => new(AccountStatus.ConfigurationError, default, message);
}
