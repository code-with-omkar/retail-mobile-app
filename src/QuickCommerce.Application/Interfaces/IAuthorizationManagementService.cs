using QuickCommerce.Application.DTOs;

namespace QuickCommerce.Application.Interfaces;

public interface IAuthorizationManagementService
{
    Task<IReadOnlyList<ManagedUserResponse>> GetUsersAsync(CancellationToken cancellationToken = default);
    Task<ManagedUserResponse?> GetUserAsync(Guid id, CancellationToken cancellationToken = default);
    Task<ManagedUserResponse?> CreateUserAsync(CreateManagedUserRequest request, CancellationToken cancellationToken = default);
    Task<ManagedUserResponse?> UpdateUserAsync(Guid id, UpdateManagedUserRequest request, CancellationToken cancellationToken = default);
    Task<ManagedUserResponse?> SetUserStatusAsync(Guid id, bool isActive, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<AuthorizationRoleResponse>> GetRolesAsync(CancellationToken cancellationToken = default);
    Task<IReadOnlyList<AuthorizationPermissionResponse>> GetPermissionsAsync(CancellationToken cancellationToken = default);
    Task<AuthorizationRoleResponse?> UpdateRolePermissionsAsync(Guid id, UpdateRolePermissionsRequest request, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<OrganizationResponse>> GetOrganizationsAsync(CancellationToken cancellationToken = default);
    Task<IReadOnlyList<StoreResponse>> GetStoresAsync(Guid? organizationId, CancellationToken cancellationToken = default);
}