using QuickCommerce.Domain;

namespace QuickCommerce.Application.DTOs;

public sealed record AuthorizationPermissionResponse(Guid Id, string Code, string Name, string? Description, bool IsActive);

public sealed record AuthorizationRoleResponse(
    Guid Id,
    string Code,
    string Name,
    string? Description,
    bool IsActive,
    IReadOnlyList<AuthorizationPermissionResponse> Permissions);

public sealed record ManagedUserResponse(
    Guid Id,
    string FirstName,
    string LastName,
    string FullName,
    string? Email,
    Guid OrganizationId,
    Guid? StoreId,
    IReadOnlyList<Guid> StoreIds,
    Role Role,
    StaffCategory? StaffCategory,
    bool IsActive,
    IReadOnlyList<AuthorizationPermissionResponse> Permissions);

public sealed record CreateManagedUserRequest(
    string FirstName,
    string LastName,
    string Email,
    Guid OrganizationId,
    Guid? StoreId,
    Role Role,
    bool IsActive,
    StaffCategory? StaffCategory = null,
    IReadOnlyList<Guid>? StoreIds = null);

public sealed record UpdateManagedUserRequest(
    string FirstName,
    string LastName,
    Guid? StoreId,
    Role Role,
    bool IsActive,
    StaffCategory? StaffCategory = null,
    IReadOnlyList<Guid>? StoreIds = null);

public sealed record UpdateRolePermissionsRequest(IReadOnlyList<Guid> PermissionIds);

public sealed record OrganizationResponse(Guid Id, string Name, bool IsActive);