using QuickCommerce.Application.DTOs;

namespace QuickCommerce.Application.Interfaces;

public interface IAuthorizationScopeService
{
    Task<AuthorizationScope?> ResolveAsync(CancellationToken cancellationToken = default);
    Task<bool> HasPermissionAsync(string permissionCode, CancellationToken cancellationToken = default);
    Task<bool> CanAccessStoreAsync(Guid storeId, CancellationToken cancellationToken = default);
    Task<bool> OwnsCustomerAsync(Guid customerId, CancellationToken cancellationToken = default);
    Task<AuthorizationScope?> ResolveForUserAsync(Guid userId, CancellationToken cancellationToken = default);
}