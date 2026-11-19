using QuickCommerce.Application.DTOs;

namespace QuickCommerce.Application.Interfaces;

public enum FavouriteStatus { Succeeded, Forbidden, NotFound, LimitReached, StoreNotFound }

public sealed record FavouriteResult<T>(FavouriteStatus Status, T? Value = default, string? Message = null)
{
    public static FavouriteResult<T> Ok(T value) => new(FavouriteStatus.Succeeded, value);
    public static FavouriteResult<T> Fail(FavouriteStatus status, string message) => new(status, default, message);
}

/// <summary>A customer's saved products. Only the customer themself can see or change their list.</summary>
public interface IFavouriteService
{
    public const int MaxFavourites = 200;

    /// <summary>The saved products still on sale, newest first (at most 60). With a store, each carries that store's stock.</summary>
    Task<FavouriteResult<IReadOnlyList<CatalogProductResponse>>> ListAsync(Guid? storeId, CancellationToken cancellationToken = default);

    /// <summary>Ids of every saved product, newest first: enough for the app to fill in its hearts.</summary>
    Task<FavouriteResult<IReadOnlyList<Guid>>> IdsAsync(CancellationToken cancellationToken = default);

    /// <summary>Saves a product. Saving one that is already saved changes nothing.</summary>
    Task<FavouriteResult<bool>> AddAsync(Guid productId, CancellationToken cancellationToken = default);

    /// <summary>Removes a product from the list. Removing one that is not there changes nothing.</summary>
    Task<FavouriteResult<bool>> RemoveAsync(Guid productId, CancellationToken cancellationToken = default);
}

public interface IFavouriteStore
{
    Task<IReadOnlyList<Guid>> ListAsync(Guid customerId, int take, CancellationToken cancellationToken = default);
    Task<int> CountAsync(Guid customerId, CancellationToken cancellationToken = default);

    /// <summary>Adds it. False when it was already there.</summary>
    Task<bool> AddAsync(Guid customerId, Guid productId, DateTime nowUtc, CancellationToken cancellationToken = default);

    Task<bool> RemoveAsync(Guid customerId, Guid productId, CancellationToken cancellationToken = default);
}
