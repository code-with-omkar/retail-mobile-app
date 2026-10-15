using QuickCommerce.Application.DTOs;

namespace QuickCommerce.Application.Interfaces;

/// <summary>A store the customer has bought from, for the "Your stores" cards on Home.</summary>
public sealed record RecentStore(Guid Id, string Name, string Address, DateTime LastOrderedAt);

public enum PurchaseHistoryStatus
{
    Succeeded,
    Forbidden,
    StoreNotFound
}

public sealed record PurchaseHistoryResult<T>(PurchaseHistoryStatus Status, T? Value = default, string? Message = null);

public interface IPurchaseHistoryStore
{
    /// <summary>Stores the user has an order from (not cancelled, not still waiting for payment), newest first. Only active stores.</summary>
    Task<IReadOnlyList<RecentStore>> RecentStoresAsync(Guid userId, int take, CancellationToken cancellationToken = default);

    /// <summary>Products the user bought at this store, most recently bought first, each once.</summary>
    Task<IReadOnlyList<Guid>> RecentProductIdsAsync(Guid userId, Guid storeId, int take, CancellationToken cancellationToken = default);
}

public interface IPurchaseHistoryService
{
    /// <summary>Up to four stores the signed-in customer bought from last.</summary>
    Task<PurchaseHistoryResult<IReadOnlyList<RecentStore>>> RecentStoresAsync(CancellationToken cancellationToken = default);

    /// <summary>What the customer bought before at this store, with today's price, offers and stock there.</summary>
    Task<PurchaseHistoryResult<IReadOnlyList<CatalogProductResponse>>> BoughtBeforeAsync(Guid storeId, CancellationToken cancellationToken = default);
}
