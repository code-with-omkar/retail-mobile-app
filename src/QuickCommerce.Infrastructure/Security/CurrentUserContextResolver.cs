using QuickCommerce.Application.DTOs;
using QuickCommerce.Application.Interfaces;

namespace QuickCommerce.Infrastructure.Security;

public sealed class CurrentUserContextResolver(ICurrentUser currentUser, ICommerceStore store, IActingOrganization? acting = null, IAuthorizationScopeService? scopes = null) : ICurrentUserContextResolver
{
    public async Task<UserContext?> ResolveAsync(CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(currentUser.UserId))
        {
            return null;
        }

        var context = await store.GetUserContextAsync(currentUser.UserId, cancellationToken);
        if (context is null ||
            currentUser.OrganizationId.HasValue && currentUser.OrganizationId != context.OrganizationId ||
            currentUser.StoreId.HasValue && currentUser.StoreId != context.StoreId)
        {
            return null;
        }

        if (acting?.Requested is not null && scopes is not null)
        {
            // A platform administrator looking at another organization works in that organization; a refused request has no context.
            var scope = await scopes.ResolveAsync(cancellationToken);
            return scope is null ? null : context with { OrganizationId = scope.OrganizationId };
        }

        return context;
    }
}