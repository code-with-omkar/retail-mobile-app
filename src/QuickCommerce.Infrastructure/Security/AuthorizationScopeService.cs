using Microsoft.EntityFrameworkCore;
using QuickCommerce.Application.DTOs;
using QuickCommerce.Application.Interfaces;
using QuickCommerce.Infrastructure.Persistence;

namespace QuickCommerce.Infrastructure.Security;

public sealed class AuthorizationScopeService(
    QuickCommerceDbContext db,
    ICurrentUser currentUser) : IAuthorizationScopeService
{
    public async Task<AuthorizationScope?> ResolveAsync(CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(currentUser.UserId))
        {
            return null;
        }

        if (Guid.TryParse(currentUser.UserId, out var userId))
        {
            return await ResolveForUserAsync(userId, cancellationToken);
        }

        var user = await db.Users.AsNoTracking()
            .Where(item => item.ExternalSubject == currentUser.UserId)
            .Select(item => item.Id)
            .SingleOrDefaultAsync(cancellationToken);
        return user == Guid.Empty ? null : await ResolveForUserAsync(user, cancellationToken);
    }

    public async Task<AuthorizationScope?> ResolveForUserAsync(Guid userId, CancellationToken cancellationToken = default)
    {
        var user = await db.Users.AsNoTracking()
            .Include(item => item.UserRoles)
                .ThenInclude(item => item.Role)
                    .ThenInclude(item => item.RolePermissions)
                        .ThenInclude(item => item.Permission)
            .Include(item => item.StoreAssignments)
            .SingleOrDefaultAsync(item => item.Id == userId && item.IsActive, cancellationToken);
        if (user is null)
        {
            return null;
        }

        var activeRoles = user.UserRoles.Where(item => item.IsActive && item.Role.IsActive).Select(item => item.Role).ToArray();
        var roleCodes = activeRoles.Select(role => role.Code).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var permissionCodes = activeRoles
            .SelectMany(role => role.RolePermissions)
            .Where(item => item.IsActive && item.Permission.IsActive)
            .Select(item => item.Permission.Code)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var storeIds = user.StoreAssignments
            .Where(item => item.IsActive && item.EffectiveFrom <= DateTime.UtcNow && (!item.EffectiveTo.HasValue || item.EffectiveTo > DateTime.UtcNow))
            .Select(item => item.StoreId)
            .ToHashSet();

        return new AuthorizationScope(user.Id, user.OrganizationId, roleCodes, storeIds, permissionCodes);
    }

    public async Task<bool> HasPermissionAsync(string permissionCode, CancellationToken cancellationToken = default) =>
        (await ResolveAsync(cancellationToken))?.HasPermission(permissionCode) == true;

    public async Task<bool> CanAccessStoreAsync(Guid storeId, CancellationToken cancellationToken = default)
    {
        var scope = await ResolveAsync(cancellationToken);
        if (scope is null)
        {
            return false;
        }

        var hasOperationalRole = scope.RoleCodes.Contains("StoreStaff") || scope.RoleCodes.Contains("DeliveryPartner");
        if (!scope.IsApplicationAdmin && scope.RoleCodes.Contains("Customer") && !hasOperationalRole)
        {
            return false;
        }

        var storeBelongsToOrganization = await db.Stores.AsNoTracking()
            .AnyAsync(store => store.Id == storeId && store.OrganizationId == scope.OrganizationId && store.IsActive, cancellationToken);
        return storeBelongsToOrganization && (scope.IsApplicationAdmin || scope.StoreIds.Contains(storeId));
    }

    public async Task<bool> OwnsCustomerAsync(Guid customerId, CancellationToken cancellationToken = default)
    {
        var scope = await ResolveAsync(cancellationToken);
        return scope is not null && await db.Customers.AsNoTracking()
            .AnyAsync(customer => customer.Id == customerId && customer.UserId == scope.UserId && customer.IsActive, cancellationToken);
    }
}