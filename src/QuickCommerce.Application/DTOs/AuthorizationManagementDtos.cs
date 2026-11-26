using System.Text.Json.Serialization;
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

/// <summary>The portal sends and reads the role and staff category by name ("StoreStaff", "StoreManager"); numbers are still accepted.</summary>
public sealed record ManagedUserResponse(
    Guid Id,
    string FirstName,
    string LastName,
    string FullName,
    string? Email,
    Guid OrganizationId,
    Guid? StoreId,
    IReadOnlyList<Guid> StoreIds,
    [property: JsonConverter(typeof(JsonStringEnumConverter))] Role Role,
    [property: JsonConverter(typeof(JsonStringEnumConverter))] StaffCategory? StaffCategory,
    bool IsActive,
    IReadOnlyList<AuthorizationPermissionResponse> Permissions);

public sealed record CreateManagedUserRequest(
    string FirstName,
    string LastName,
    string Email,
    Guid OrganizationId,
    Guid? StoreId,
    [property: JsonConverter(typeof(JsonStringEnumConverter))] Role Role,
    bool IsActive,
    [property: JsonConverter(typeof(JsonStringEnumConverter))] StaffCategory? StaffCategory = null,
    IReadOnlyList<Guid>? StoreIds = null);

public sealed record UpdateManagedUserRequest(
    string FirstName,
    string LastName,
    Guid? StoreId,
    [property: JsonConverter(typeof(JsonStringEnumConverter))] Role Role,
    bool IsActive,
    [property: JsonConverter(typeof(JsonStringEnumConverter))] StaffCategory? StaffCategory = null,
    IReadOnlyList<Guid>? StoreIds = null);

public sealed record UpdateRolePermissionsRequest(IReadOnlyList<Guid> PermissionIds);

public sealed record OrganizationResponse(Guid Id, string Name, bool IsActive);

/// <summary>What the portal's user list asks for. Role is a role code ("ApplicationAdmin" covers administrators); Sort is "name", "name-desc", "email" or "newest".</summary>
public sealed record ManagedUserQuery(string? Search = null, string? Role = null, bool? Active = null, string? Sort = null, int Page = 1, int PageSize = 20)
{
    public const int MaxPageSize = 100;
    public ManagedUserQuery Clamped() => this with { Search = string.IsNullOrWhiteSpace(Search) ? null : Search.Trim(), Page = Math.Max(1, Page), PageSize = Math.Clamp(PageSize, 1, MaxPageSize) };
}

/// <summary>One page of users, with the totals for the whole organization (not only what matched).</summary>
public sealed record ManagedUserPage(IReadOnlyList<ManagedUserResponse> Items, int TotalCount, int Page, int PageSize, int AllCount, int ActiveCount, int RoleCount);
