using QuickCommerce.Application.DTOs;
using QuickCommerce.Domain;

namespace QuickCommerce.Application.Interfaces;

public interface ICommerceStore
{
    Task<AdminDashboardSnapshot> GetAdminDashboardAsync(Guid organizationId, IReadOnlySet<Guid> storeIds, bool isApplicationAdmin, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<Category>> GetCategoriesAsync(CancellationToken cancellationToken = default);
    Task<IReadOnlyList<Product>> GetProductsAsync(string? search, Guid? categoryId, CancellationToken cancellationToken = default);
    Task<Product?> GetProductAsync(Guid id, CancellationToken cancellationToken = default);
    /// <summary>Translations for the given products in a single query.</summary>
    /// <summary>Stock rows of one store for the given variants, in a single query.</summary>
    Task<IReadOnlyList<StoreVariantInventory>> GetStoreVariantInventoryAsync(Guid storeId, IReadOnlyCollection<Guid> variantIds, CancellationToken cancellationToken = default);
    /// <summary>Every variant (active or not) of the given products, in a single query, ordered by sort order.</summary>
    Task<IReadOnlyList<ProductVariant>> GetVariantsAsync(IReadOnlyCollection<Guid> productIds, CancellationToken cancellationToken = default);
    Task<ProductVariant?> GetVariantAsync(Guid variantId, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<ProductTranslation>> GetProductTranslationsAsync(IReadOnlyCollection<Guid> productIds, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<CategoryTranslation>> GetCategoryTranslationsAsync(CancellationToken cancellationToken = default);
    /// <summary>Active products only, filtered and paged in the data store, ordered by name then id so pages are stable.</summary>
    Task<(IReadOnlyList<Product> Items, int TotalCount)> GetActiveProductPageAsync(string? search, Guid? categoryId, int skip, int take, CancellationToken cancellationToken = default);
    /// <summary>Like <see cref="GetActiveProductPageAsync"/> but only products the store carries (has a stock row for any variant of), whatever the quantity.</summary>
    Task<(IReadOnlyList<Product> Items, int TotalCount)> GetStoreProductPageAsync(Guid storeId, string? search, Guid? categoryId, int skip, int take, CancellationToken cancellationToken = default);
    /// <summary>Category ids of the active products a store carries.</summary>
    Task<IReadOnlyList<Guid>> GetCarriedCategoryIdsAsync(Guid storeId, CancellationToken cancellationToken = default);
    /// <summary>Ids of the stores that carry at least one active product.</summary>
    Task<IReadOnlyList<Guid>> GetStoreIdsCarryingProductsAsync(CancellationToken cancellationToken = default);
    Task<IReadOnlyList<Store>> GetStoresAsync(CancellationToken cancellationToken = default);
    Task<IReadOnlyList<Store>> GetScopedStoresAsync(Guid organizationId, IReadOnlySet<Guid> storeIds, bool isApplicationAdmin, CancellationToken cancellationToken = default);
    /// <summary>All variant stock rows. Used where a store must be chosen for a whole order.</summary>
    Task<IReadOnlyList<StoreVariantInventory>> GetVariantInventoryAsync(CancellationToken cancellationToken = default);
    Task<IReadOnlyList<Order>> GetOrdersAsync(CancellationToken cancellationToken = default);
    Task<IReadOnlyList<Order>> GetScopedOrdersAsync(Guid organizationId, IReadOnlySet<Guid> storeIds, bool isApplicationAdmin, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<AdminOrderResponse>> GetScopedOrderSummariesAsync(Guid organizationId, IReadOnlySet<Guid> storeIds, bool isApplicationAdmin, CancellationToken cancellationToken = default);
    Task<Order?> GetOrderAsync(Guid id, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<Order>> GetCustomerOrdersAsync(Guid userId, Guid organizationId, CancellationToken cancellationToken = default);
    Task<Order?> GetCustomerOrderAsync(Guid orderId, Guid userId, Guid organizationId, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<Notification>> GetNotificationsAsync(Guid customerId, bool unreadOnly, CancellationToken cancellationToken = default);
    Task<bool> SetNotificationReadAsync(Guid notificationId, Guid customerId, bool isRead, CancellationToken cancellationToken = default);
    Task<UserContext?> GetUserContextAsync(string externalSubject, CancellationToken cancellationToken = default);
    Task<Customer?> GetCustomerByUserIdAsync(Guid userId, CancellationToken cancellationToken = default);
    Task<Store?> GetStoreAsync(Guid id, CancellationToken cancellationToken = default);
    Task<Cart?> GetCartAsync(Guid customerId, Guid storeId, CancellationToken cancellationToken = default);
    Task AddCartAsync(Cart cart, CancellationToken cancellationToken = default);
    Task SaveCartAsync(Cart cart, CancellationToken cancellationToken = default);
    Task<CheckoutCommitResult> TryCheckoutCartAsync(Guid customerId, Guid storeId, CheckoutRequest request, CancellationToken cancellationToken = default);
    Task<OrderLifecycleResult> TryTransitionOrderAsync(Guid orderId, Guid organizationId, Guid? storeId, OrderStatus targetStatus, CancellationToken cancellationToken = default);
    Task<bool> TryCreateOrderAsync(Order order, IReadOnlyCollection<InventoryAdjustment> inventoryAdjustments, CancellationToken cancellationToken = default);
}
