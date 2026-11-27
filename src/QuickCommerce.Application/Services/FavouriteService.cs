using QuickCommerce.Application.DTOs;
using QuickCommerce.Application.Interfaces;
using QuickCommerce.Domain;

namespace QuickCommerce.Application.Services;

public sealed class FavouriteService(
    IFavouriteStore store,
    ICommerceStore data,
    ICatalogService catalogue,
    ICurrentUserContextResolver contextResolver,
    TimeProvider clock) : IFavouriteService
{
    private const int ListSize = 60;

    private async Task<Guid?> CustomerAsync(CancellationToken cancellationToken)
    {
        var context = await contextResolver.ResolveAsync(cancellationToken);
        if (context is not { Role: Role.Customer })
        {
            return null;
        }

        var customer = await data.GetCustomerByUserIdAsync(context.UserId, cancellationToken);
        return customer is { IsActive: true } ? customer.Id : null;
    }

    private static FavouriteResult<T> Refused<T>() => FavouriteResult<T>.Fail(FavouriteStatus.Forbidden, "Sign in to save products.");

    public async Task<FavouriteResult<IReadOnlyList<CatalogProductResponse>>> ListAsync(Guid? storeId, CancellationToken cancellationToken = default)
    {
        if (await CustomerAsync(cancellationToken) is not { } customerId)
        {
            return Refused<IReadOnlyList<CatalogProductResponse>>();
        }

        var products = new List<CatalogProductResponse>();
        foreach (var id in await store.ListAsync(customerId, ListSize, cancellationToken))
        {
            if (storeId is { } chosen)
            {
                var result = await catalogue.GetCatalogProductForStoreAsync(id, chosen, cancellationToken);
                if (result.Status == CatalogQueryStatus.StoreNotFound)
                {
                    return FavouriteResult<IReadOnlyList<CatalogProductResponse>>.Fail(FavouriteStatus.StoreNotFound, "Store not found.");
                }

                if (result.Product is not null)
                {
                    products.Add(result.Product);
                }
            }
            else if (await catalogue.GetCatalogProductAsync(id, cancellationToken) is { } product)
            {
                products.Add(product);
            }
        }

        return FavouriteResult<IReadOnlyList<CatalogProductResponse>>.Ok(products);
    }

    public async Task<FavouriteResult<IReadOnlyList<Guid>>> IdsAsync(CancellationToken cancellationToken = default) =>
        await CustomerAsync(cancellationToken) is { } customerId
            ? FavouriteResult<IReadOnlyList<Guid>>.Ok(await store.ListAsync(customerId, IFavouriteService.MaxFavourites, cancellationToken))
            : Refused<IReadOnlyList<Guid>>();

    public async Task<FavouriteResult<bool>> AddAsync(Guid productId, CancellationToken cancellationToken = default)
    {
        if (await CustomerAsync(cancellationToken) is not { } customerId)
        {
            return Refused<bool>();
        }

        if (await data.GetProductAsync(productId, cancellationToken) is not { IsActive: true })
        {
            return FavouriteResult<bool>.Fail(FavouriteStatus.NotFound, "Product not found.");
        }

        if (await store.CountAsync(customerId, cancellationToken) >= IFavouriteService.MaxFavourites)
        {
            // Saving one that is already there is still fine at the limit.
            return (await store.ListAsync(customerId, IFavouriteService.MaxFavourites, cancellationToken)).Contains(productId)
                ? FavouriteResult<bool>.Ok(true)
                : FavouriteResult<bool>.Fail(FavouriteStatus.LimitReached, $"You can save up to {IFavouriteService.MaxFavourites} products. Remove one first.");
        }

        await store.AddAsync(customerId, productId, clock.GetUtcNow().UtcDateTime, cancellationToken);
        return FavouriteResult<bool>.Ok(true);
    }

    public async Task<FavouriteResult<bool>> RemoveAsync(Guid productId, CancellationToken cancellationToken = default)
    {
        if (await CustomerAsync(cancellationToken) is not { } customerId)
        {
            return Refused<bool>();
        }

        await store.RemoveAsync(customerId, productId, cancellationToken);
        return FavouriteResult<bool>.Ok(true);
    }
}
