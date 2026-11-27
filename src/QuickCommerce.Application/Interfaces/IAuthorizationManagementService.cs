using QuickCommerce.Application.DTOs;

namespace QuickCommerce.Application.Interfaces;

public interface IAuthorizationManagementService
{
    Task<IReadOnlyList<ManagedUserResponse>> GetUsersAsync(CancellationToken cancellationToken = default);
    Task<ManagedUserPage> GetUsersPageAsync(ManagedUserQuery query, CancellationToken cancellationToken = default);

    /// <summary>Gives a staff account a new temporary password and signs it out everywhere. Null when the caller may not, or it is a customer or the caller.</summary>
    Task<TemporaryPasswordResponse?> SetTemporaryPasswordAsync(Guid userId, CancellationToken cancellationToken = default);
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