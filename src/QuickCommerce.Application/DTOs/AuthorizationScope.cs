namespace QuickCommerce.Application.DTOs;

public sealed record AuthorizationScope(
    Guid UserId,
    Guid OrganizationId,
    IReadOnlySet<string> RoleCodes,
    IReadOnlySet<Guid> StoreIds,
    IReadOnlySet<string> PermissionCodes)
{
    public bool IsApplicationAdmin => RoleCodes.Contains("ApplicationAdmin") || RoleCodes.Contains("Admin");

    public bool HasPermission(string permissionCode) => IsApplicationAdmin || PermissionCodes.Contains(permissionCode);

    public bool CanAccessStore(Guid storeId) => IsApplicationAdmin || StoreIds.Contains(storeId);
}