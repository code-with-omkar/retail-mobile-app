using QuickCommerce.Domain;

namespace QuickCommerce.Application.DTOs;

public sealed record LoginRequest(string Username, string Password);

public sealed record RefreshRequest(string RefreshToken);

public sealed record AuthenticationUserResponse(
    Guid Id,
    string DisplayName,
    string? Email,
    Guid OrganizationId,
    IReadOnlyList<string> Roles,
    IReadOnlyList<string> Permissions,
    StaffCategory? StaffCategory = null);

public sealed record AuthenticationResponse(
    string AccessToken,
    string RefreshToken,
    int ExpiresIn,
    string TokenType,
    AuthenticationUserResponse User);

public sealed record AuthenticationResult(bool Succeeded, AuthenticationResponse? Response = null, string? Error = null)
{
    public static AuthenticationResult Failure(string message) => new(false, Error: message);
    public static AuthenticationResult Success(AuthenticationResponse response) => new(true, response);
}