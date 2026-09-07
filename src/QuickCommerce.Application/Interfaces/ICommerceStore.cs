using QuickCommerce.Application.DTOs;
using QuickCommerce.Domain;

namespace QuickCommerce.Application.Interfaces;

public interface ICommerceStore
{
    Task<IReadOnlyList<Category>> GetCategoriesAsync(CancellationToken cancellationToken = default);
    Task<IReadOnlyList<Product>> GetProductsAsync(string? search, Guid? categoryId, CancellationToken cancellationToken = default);
    Task<Product?> GetProductAsync(Guid id, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<Store>> GetStoresAsync(CancellationToken cancellationToken = default);
    Task<IReadOnlyList<StoreInventory>> GetInventoryAsync(CancellationToken cancellationToken = default);
    Task<IReadOnlyList<Order>> GetOrdersAsync(CancellationToken cancellationToken = default);
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
