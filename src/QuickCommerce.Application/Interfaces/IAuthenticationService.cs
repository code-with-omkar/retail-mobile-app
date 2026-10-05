using QuickCommerce.Application.DTOs;

namespace QuickCommerce.Application.Interfaces;

public interface IAuthenticationService
{
    Task<AuthenticationResult> LoginAsync(LoginRequest request, string? ipAddress, CancellationToken cancellationToken = default);
    Task<AuthenticationResult> RefreshAsync(RefreshRequest request, string? ipAddress, CancellationToken cancellationToken = default);
    Task<bool> LogoutAsync(string refreshToken, string? ipAddress, CancellationToken cancellationToken = default);
    Task<AuthenticationUserResponse?> GetCurrentUserAsync(CancellationToken cancellationToken = default);
}