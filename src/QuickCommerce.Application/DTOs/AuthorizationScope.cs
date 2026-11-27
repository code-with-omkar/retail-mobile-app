namespace QuickCommerce.Application.DTOs;

public sealed record AuthorizationScope(
    Guid UserId,
    Guid OrganizationId,
    IReadOnlySet<string> RoleCodes,
    IReadOnlySet<Guid> StoreIds,
    IReadOnlySet<string> PermissionCodes,
    Guid? ActingFromOrganizationId = null)
{
    public bool IsApplicationAdmin => RoleCodes.Contains("ApplicationAdmin") || RoleCodes.Contains("Admin");

    /// <summary>Only the platform role may look at other organizations (an ordinary organization admin may not).</summary>
    public bool IsPlatformAdmin => RoleCodes.Contains("ApplicationAdmin");

    public bool HasPermission(string permissionCode) => IsApplicationAdmin || PermissionCodes.Contains(permissionCode);

    public bool CanAccessStore(Guid storeId) => IsApplicationAdmin || StoreIds.Contains(storeId);
}