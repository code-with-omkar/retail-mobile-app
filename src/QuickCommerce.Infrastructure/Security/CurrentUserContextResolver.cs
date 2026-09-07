using QuickCommerce.Application.DTOs;
using QuickCommerce.Application.Interfaces;

namespace QuickCommerce.Infrastructure.Security;

public sealed class CurrentUserContextResolver(ICurrentUser currentUser, ICommerceStore store) : ICurrentUserContextResolver
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

        return context;
    }
}