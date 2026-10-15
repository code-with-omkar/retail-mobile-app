using QuickCommerce.Application.DTOs;
using QuickCommerce.Application.Interfaces;
using QuickCommerce.Domain;

namespace QuickCommerce.Application.Services;

/// <summary>What the signed-in customer bought before. The customer comes from the token, never from the request.</summary>
public sealed class PurchaseHistoryService(
    IPurchaseHistoryStore store,
    ICatalogService catalogue,
    ICurrentUserContextResolver contextResolver) : IPurchaseHistoryService
{
    public const int StoreCards = 4;
    public const int BoughtBeforeSize = 20;

    private async Task<Guid?> UserAsync(CancellationToken cancellationToken)
    {
        var context = await contextResolver.ResolveAsync(cancellationToken);
        return context is { Role: Role.Customer } ? context.UserId : null;
    }

    public async Task<PurchaseHistoryResult<IReadOnlyList<RecentStore>>> RecentStoresAsync(CancellationToken cancellationToken = default) =>
        await UserAsync(cancellationToken) is { } userId
            ? new(PurchaseHistoryStatus.Succeeded, await store.RecentStoresAsync(userId, StoreCards, cancellationToken))
            : new(PurchaseHistoryStatus.Forbidden, Message: "Sign in to see your stores.");

    public async Task<PurchaseHistoryResult<IReadOnlyList<CatalogProductResponse>>> BoughtBeforeAsync(Guid storeId, CancellationToken cancellationToken = default)
    {
        if (await UserAsync(cancellationToken) is not { } userId)
        {
            return new(PurchaseHistoryStatus.Forbidden, Message: "Sign in to see what you bought.");
        }

        var products = new List<CatalogProductResponse>();
        foreach (var id in await store.RecentProductIdsAsync(userId, storeId, BoughtBeforeSize, cancellationToken))
        {
            var result = await catalogue.GetCatalogProductForStoreAsync(id, storeId, cancellationToken);
            if (result.Status == CatalogQueryStatus.StoreNotFound)
            {
                return new(PurchaseHistoryStatus.StoreNotFound, Message: "Store not found.");
            }

            // A product that is gone from the shop is simply left out.
            if (result.Product is not null)
            {
                products.Add(result.Product);
            }
        }

        return new(PurchaseHistoryStatus.Succeeded, products);
    }
}
