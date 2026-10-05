using Microsoft.EntityFrameworkCore;
using QuickCommerce.Application.DTOs;
using QuickCommerce.Application.Interfaces;
using QuickCommerce.Domain;
using QuickCommerce.Infrastructure.Persistence;

namespace QuickCommerce.Infrastructure.Security;

public sealed class AuthorizationManagementService(
    QuickCommerceDbContext db,
    ICurrentUserContextResolver currentUserContextResolver) : IAuthorizationManagementService
{
    public async Task<IReadOnlyList<ManagedUserResponse>> GetUsersAsync(CancellationToken cancellationToken = default)
    {
        var context = await currentUserContextResolver.ResolveAsync(cancellationToken);
        if (!CanManageAuthorization(context))
        {
            return [];
        }

        var users = await db.Users.AsNoTracking()
            .Include(user => user.UserRoles)
                .ThenInclude(userRole => userRole.Role)
                    .ThenInclude(role => role.RolePermissions)
                        .ThenInclude(rolePermission => rolePermission.Permission)
            .Where(user => user.OrganizationId == context!.OrganizationId)
            .OrderBy(user => user.DisplayName)
            .ToListAsync(cancellationToken);
        return users.Select(MapUser).ToArray();
    }

    public async Task<ManagedUserResponse?> GetUserAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var context = await currentUserContextResolver.ResolveAsync(cancellationToken);
        var user = !CanManageAuthorization(context)
            ? null
            : await db.Users
                .Include(item => item.UserRoles)
                    .ThenInclude(userRole => userRole.Role)
                        .ThenInclude(role => role.RolePermissions)
                            .ThenInclude(rolePermission => rolePermission.Permission)
                .SingleOrDefaultAsync(item => item.Id == id && item.OrganizationId == context!.OrganizationId, cancellationToken);
        return user is null ? null : MapUser(user);
    }

    public async Task<ManagedUserResponse?> CreateUserAsync(CreateManagedUserRequest request, CancellationToken cancellationToken = default)
    {
        var context = await currentUserContextResolver.ResolveAsync(cancellationToken);
        if (!CanManageAuthorization(context) || context!.OrganizationId != request.OrganizationId ||
            await db.Users.AnyAsync(user => user.OrganizationId == request.OrganizationId && user.Email == request.Email, cancellationToken))
        {
            return null;
        }

        var requestedStoreIds = (request.StoreIds ?? []).Concat(request.StoreId.HasValue ? [request.StoreId.Value] : []).Distinct().ToArray();
        if (request.Role == Role.StoreStaff && request.StaffCategory is null || request.Role != Role.StoreStaff && request.StaffCategory is not null || requestedStoreIds.Any() && await db.Stores.CountAsync(store => requestedStoreIds.Contains(store.Id) && store.OrganizationId == request.OrganizationId, cancellationToken) != requestedStoreIds.Length)
        {
            return null;
        }

        var user = new User
        {
            ExternalSubject = request.Email.Trim().ToLowerInvariant(),
            DisplayName = $"{request.FirstName.Trim()} {request.LastName.Trim()}",
            FirstName = request.FirstName.Trim(),
            LastName = request.LastName.Trim(),
            Email = request.Email.Trim(),
            OrganizationId = request.OrganizationId,
            StoreId = request.StoreId,
            Role = request.Role,
            StaffCategory = request.Role == Role.StoreStaff ? request.StaffCategory : null,
            IsActive = request.IsActive
        };
        db.Users.Add(user);
        var authorizationRole = await db.AuthorizationRoles.SingleOrDefaultAsync(role => role.Code == RoleCode(request.Role) && role.IsActive, cancellationToken);
        if (authorizationRole is null)
        {
            return null;
        }

        user.UserRoles.Add(new UserRole { UserId = user.Id, RoleId = authorizationRole.Id, IsActive = true });
        var storeIds = requestedStoreIds;
        if (request.StoreId.HasValue && !storeIds.Contains(request.StoreId.Value))
        {
            storeIds = [request.StoreId.Value, .. storeIds];
        }
        foreach (var storeId in storeIds)
        {
            user.StoreAssignments.Add(new UserStoreAssignment { UserId = user.Id, StoreId = storeId, IsActive = true });
        }
        if (request.Role == Role.Customer)
        {
            user.Customer = new Customer { UserId = user.Id, User = user };
        }

        await db.SaveChangesAsync(cancellationToken);
        return MapUser(user);
    }

    public async Task<ManagedUserResponse?> UpdateUserAsync(Guid id, UpdateManagedUserRequest request, CancellationToken cancellationToken = default)
    {
        var user = await GetScopedUserAsync(id, cancellationToken);
        var requestedStoreIds = (request.StoreIds ?? []).Concat(request.StoreId.HasValue ? [request.StoreId.Value] : []).Distinct().ToArray();
        if (user is null || request.Role == Role.StoreStaff && request.StaffCategory is null || request.Role != Role.StoreStaff && request.StaffCategory is not null || requestedStoreIds.Any() && await db.Stores.CountAsync(store => requestedStoreIds.Contains(store.Id) && store.OrganizationId == user.OrganizationId, cancellationToken) != requestedStoreIds.Length)
        {
            return null;
        }

        user.FirstName = request.FirstName.Trim();
        user.LastName = request.LastName.Trim();
        user.DisplayName = $"{user.FirstName} {user.LastName}";
        user.StoreId = request.StoreId;
        user.Role = request.Role;
        user.StaffCategory = request.Role == Role.StoreStaff ? request.StaffCategory : null;
        user.IsActive = request.IsActive;
        var authorizationRole = await db.AuthorizationRoles.SingleOrDefaultAsync(role => role.Code == RoleCode(request.Role) && role.IsActive, cancellationToken);
        if (authorizationRole is null)
        {
            return null;
        }

        foreach (var userRole in user.UserRoles)
        {
            userRole.IsActive = userRole.RoleId == authorizationRole.Id;
        }

        var requestedStoreIdSet = requestedStoreIds.ToHashSet();
        if (request.StoreId.HasValue)
        {
            requestedStoreIdSet.Add(request.StoreId.Value);
        }
        var keepsStoreAssignment = request.Role is Role.StoreStaff or Role.DeliveryPartner;
        foreach (var assignment in user.StoreAssignments)
        {
            assignment.IsActive = keepsStoreAssignment && requestedStoreIdSet.Contains(assignment.StoreId);
        }

        var existingAssignments = user.StoreAssignments.Select(assignment => assignment.StoreId).ToHashSet();
        foreach (var storeId in requestedStoreIdSet.Where(storeId => !existingAssignments.Contains(storeId)))
        {
            user.StoreAssignments.Add(new UserStoreAssignment { UserId = user.Id, StoreId = storeId, IsActive = keepsStoreAssignment });
        }
        await db.SaveChangesAsync(cancellationToken);
        return MapUser(user);
    }

    public async Task<ManagedUserResponse?> SetUserStatusAsync(Guid id, bool isActive, CancellationToken cancellationToken = default)
    {
        var user = await GetScopedUserAsync(id, cancellationToken);
        if (user is null)
        {
            return null;
        }

        user.IsActive = isActive;
        await db.SaveChangesAsync(cancellationToken);
        return MapUser(user);
    }

    public async Task<IReadOnlyList<AuthorizationRoleResponse>> GetRolesAsync(CancellationToken cancellationToken = default)
    {
        if (!await IsAdminAsync(cancellationToken))
        {
            return [];
        }

        return await db.AuthorizationRoles.AsNoTracking().Include(role => role.RolePermissions).ThenInclude(item => item.Permission)
            .OrderBy(role => role.Name).Select(role => MapRole(role)).ToListAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<AuthorizationPermissionResponse>> GetPermissionsAsync(CancellationToken cancellationToken = default)
    {
        if (!await IsAdminAsync(cancellationToken))
        {
            return [];
        }

        return await db.AuthorizationPermissions.AsNoTracking().OrderBy(permission => permission.Code)
            .Select(permission => new AuthorizationPermissionResponse(permission.Id, permission.Code, permission.Name, permission.Description, permission.IsActive))
            .ToListAsync(cancellationToken);
    }

    public async Task<AuthorizationRoleResponse?> UpdateRolePermissionsAsync(Guid id, UpdateRolePermissionsRequest request, CancellationToken cancellationToken = default)
    {
        if (!await IsAdminAsync(cancellationToken))
        {
            return null;
        }

        var role = await db.AuthorizationRoles.Include(item => item.RolePermissions).ThenInclude(item => item.Permission).SingleOrDefaultAsync(item => item.Id == id, cancellationToken);
        var permissionIds = await db.AuthorizationPermissions.Where(permission => request.PermissionIds.Contains(permission.Id) && permission.IsActive).Select(permission => permission.Id).ToListAsync(cancellationToken);
        if (role is null || permissionIds.Count != request.PermissionIds.Distinct().Count())
        {
            return null;
        }

        role.RolePermissions.Clear();
        foreach (var permissionId in permissionIds)
        {
            role.RolePermissions.Add(new RolePermission { RoleId = role.Id, PermissionId = permissionId });
        }

        await db.SaveChangesAsync(cancellationToken);
        return MapRole(role);
    }

    public async Task<IReadOnlyList<OrganizationResponse>> GetOrganizationsAsync(CancellationToken cancellationToken = default)
    {
        if (!await IsAdminAsync(cancellationToken))
        {
            return [];
        }

        return await db.Organizations.AsNoTracking().Where(organization => organization.IsActive)
            .OrderBy(organization => organization.Name)
            .Select(organization => new OrganizationResponse(organization.Id, organization.Name, organization.IsActive))
            .ToListAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<StoreResponse>> GetStoresAsync(Guid? organizationId, CancellationToken cancellationToken = default)
    {
        var context = await currentUserContextResolver.ResolveAsync(cancellationToken);
        if (!CanManageAuthorization(context) || organizationId.HasValue && organizationId != context!.OrganizationId)
        {
            return [];
        }

        return await db.Stores.AsNoTracking().Where(store => store.OrganizationId == context!.OrganizationId && store.IsActive)
            .OrderBy(store => store.Name)
            .Select(store => new StoreResponse(store.Id, store.Name, store.Address, store.Latitude, store.Longitude, store.ServiceRadiusKm, store.IsActive))
            .ToListAsync(cancellationToken);
    }

    private async Task<User?> GetScopedUserAsync(Guid id, CancellationToken cancellationToken)
    {
        var context = await currentUserContextResolver.ResolveAsync(cancellationToken);
        return !CanManageAuthorization(context)
            ? null
            : await db.Users
                .Include(user => user.UserRoles)
                    .ThenInclude(userRole => userRole.Role)
                        .ThenInclude(role => role.RolePermissions)
                            .ThenInclude(rolePermission => rolePermission.Permission)
                .Include(user => user.StoreAssignments)
                .SingleOrDefaultAsync(user => user.Id == id && user.OrganizationId == context!.OrganizationId, cancellationToken);
    }

    private async Task<bool> IsAdminAsync(CancellationToken cancellationToken) => CanManageAuthorization(await currentUserContextResolver.ResolveAsync(cancellationToken));

    private static bool CanManageAuthorization(UserContext? context) => context is not null && (context.Role == Role.Admin || context.Role == Role.ApplicationAdmin);

    private static ManagedUserResponse MapUser(User user) => new(
        user.Id,
        user.FirstName ?? user.DisplayName,
        user.LastName ?? string.Empty,
        user.DisplayName,
        user.Email,
        user.OrganizationId,
        user.StoreId,
        user.StoreAssignments.Where(assignment => assignment.IsActive).Select(assignment => assignment.StoreId).ToArray(),
        user.Role,
        user.StaffCategory,
        user.IsActive,
        user.UserRoles
            .Where(userRole => userRole.IsActive && userRole.Role.IsActive)
            .SelectMany(userRole => userRole.Role.RolePermissions)
            .Where(rolePermission => rolePermission.IsActive && rolePermission.Permission.IsActive)
            .Select(rolePermission => new AuthorizationPermissionResponse(
                rolePermission.Permission.Id,
                rolePermission.Permission.Code,
                rolePermission.Permission.Name,
                rolePermission.Permission.Description,
                rolePermission.Permission.IsActive))
            .DistinctBy(permission => permission.Id)
            .ToArray());

    private static string RoleCode(Role role) => role == Role.Admin ? "ApplicationAdmin" : role.ToString();

    private static AuthorizationRoleResponse MapRole(AuthorizationRole role) => new(
        role.Id,
        role.Code,
        role.Name,
        role.Description,
        role.IsActive,
        role.RolePermissions.Where(item => item.Permission.IsActive).Select(item => new AuthorizationPermissionResponse(item.Permission.Id, item.Permission.Code, item.Permission.Name, item.Permission.Description, item.Permission.IsActive)).ToArray());
}