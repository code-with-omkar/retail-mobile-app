using QuickCommerce.Application.Services;
using QuickCommerce.Application.Interfaces;
using QuickCommerce.Application.DTOs;
using QuickCommerce.Domain;

namespace QuickCommerce.Infrastructure;

public sealed class InMemoryCommerceStore : ICommerceStore
{
    public List<Category> Categories { get; } = [];
    public List<Organization> Organizations { get; } = [];
    public List<User> Users { get; } = [];
    public List<Customer> Customers { get; } = [];
    public List<Notification> Notifications { get; } = [];
    public List<Product> Products { get; } = [];
    public List<ProductTranslation> ProductTranslations { get; } = [];
    public List<CategoryTranslation> CategoryTranslations { get; } = [];
    public List<Store> Stores { get; } = [];
    public List<ProductVariant> Variants { get; } = [];
    public List<StoreVariantInventory> Inventory { get; } = [];
    public List<Cart> Carts { get; } = [];
    public List<Order> Orders { get; } = [];
    public object SyncRoot { get; } = new();

    public Task<AdminDashboardSnapshot> GetAdminDashboardAsync(Guid organizationId, IReadOnlySet<Guid> storeIds, bool isApplicationAdmin, CancellationToken cancellationToken = default)
    {
        var scopedStores = Stores.Where(store => store.IsActive && store.OrganizationId == organizationId && (isApplicationAdmin || storeIds.Contains(store.Id))).ToArray();
        var scopedStoreIds = scopedStores.Select(store => store.Id).ToHashSet();
        var scopedOrders = Orders.Where(order => scopedStoreIds.Contains(order.StoreId)).OrderByDescending(order => order.CreatedAt).ToArray();
        var today = DateTime.UtcNow.Date;
        var activeStatuses = new[] { OrderStatus.Pending, OrderStatus.Accepted, OrderStatus.Preparing, OrderStatus.Ready, OrderStatus.Confirmed, OrderStatus.OutForDelivery };
        var activeOrders = scopedOrders.Where(order => activeStatuses.Contains(order.Status)).Select(order => new ActiveOrderSummary(
            order.Id,
            order.OrderNumber,
            Users.FirstOrDefault(user => user.Id == order.UserId)?.DisplayName ?? "Unknown customer",
            Stores.First(store => store.Id == order.StoreId).Name,
            order.Status,
            order.CreatedAt,
            order.TotalAmount,
            null)).ToArray();
        var completedOrders = scopedOrders.Where(order => order.Status == OrderStatus.Completed).ToArray();
        var totalStores = Stores.Count(store => store.OrganizationId == organizationId && (isApplicationAdmin || storeIds.Contains(store.Id)));
        return Task.FromResult(new AdminDashboardSnapshot(
            totalStores,
            scopedStores.Length,
            Products.Count,
            Products.Count(product => product.IsActive),
            Customers.Count(customer => customer.IsActive && Users.Any(user => user.Id == customer.UserId && user.OrganizationId == organizationId)),
            scopedOrders.Length,
            scopedOrders.Count(order => order.CreatedAt >= today),
            activeOrders.Length,
            completedOrders.Length,
            scopedOrders.Count(order => order.Status == OrderStatus.Pending),
            scopedOrders.Count(order => order.Status == OrderStatus.Cancelled),
            completedOrders.Sum(order => order.TotalAmount),
            completedOrders.Where(order => order.CreatedAt >= today).Sum(order => order.TotalAmount),
            activeOrders));
    }

    public Task<IReadOnlyList<Category>> GetCategoriesAsync(CancellationToken cancellationToken = default) => Task.FromResult<IReadOnlyList<Category>>(Categories.ToArray());

    public Task<IReadOnlyList<Product>> GetProductsAsync(string? search, Guid? categoryId, CancellationToken cancellationToken = default) => Task.FromResult<IReadOnlyList<Product>>(Products
        .Where(product => string.IsNullOrWhiteSpace(search) || product.Name.Contains(search, StringComparison.OrdinalIgnoreCase) || product.Description.Contains(search, StringComparison.OrdinalIgnoreCase))
        .Where(product => !categoryId.HasValue || product.CategoryId == categoryId)
        .ToArray());

    public Task<(IReadOnlyList<Product> Items, int TotalCount)> GetActiveProductPageAsync(string? search, Guid? categoryId, int skip, int take, CancellationToken cancellationToken = default)
    {
        var query = Products
            .Where(product => product.IsActive)
            .Where(product => string.IsNullOrWhiteSpace(search)
                || product.Name.Contains(search, StringComparison.OrdinalIgnoreCase)
                || product.Description.Contains(search, StringComparison.OrdinalIgnoreCase)
                || ProductTranslations.Any(translation => translation.ProductId == product.Id
                    && (translation.Name.Contains(search, StringComparison.OrdinalIgnoreCase) || (translation.Description?.Contains(search, StringComparison.OrdinalIgnoreCase) ?? false))))
            .Where(product => !categoryId.HasValue || product.CategoryId == categoryId)
            .ToArray();
        var items = query.OrderBy(product => product.Name, StringComparer.OrdinalIgnoreCase).ThenBy(product => product.Id).Skip(skip).Take(take).ToArray();
        return Task.FromResult<(IReadOnlyList<Product>, int)>((items, query.Length));
    }

    public Task<(IReadOnlyList<Product> Items, int TotalCount)> GetStoreProductPageAsync(Guid storeId, string? search, Guid? categoryId, int skip, int take, CancellationToken cancellationToken = default)
    {
        var carried = CarriedProductIds(storeId);
        var query = Products
            .Where(product => product.IsActive && carried.Contains(product.Id))
            .Where(product => string.IsNullOrWhiteSpace(search)
                || product.Name.Contains(search, StringComparison.OrdinalIgnoreCase)
                || product.Description.Contains(search, StringComparison.OrdinalIgnoreCase)
                || ProductTranslations.Any(translation => translation.ProductId == product.Id
                    && (translation.Name.Contains(search, StringComparison.OrdinalIgnoreCase) || (translation.Description?.Contains(search, StringComparison.OrdinalIgnoreCase) ?? false))))
            .Where(product => !categoryId.HasValue || product.CategoryId == categoryId)
            .ToArray();
        var items = query.OrderBy(product => product.Name, StringComparer.OrdinalIgnoreCase).ThenBy(product => product.Id).Skip(skip).Take(take).ToArray();
        return Task.FromResult<(IReadOnlyList<Product>, int)>((items, query.Length));
    }

    public Task<IReadOnlyList<Guid>> GetCarriedCategoryIdsAsync(Guid storeId, CancellationToken cancellationToken = default)
    {
        var carried = CarriedProductIds(storeId);
        return Task.FromResult<IReadOnlyList<Guid>>(Products.Where(product => product.IsActive && carried.Contains(product.Id)).Select(product => product.CategoryId).Distinct().ToArray());
    }

    public Task<IReadOnlyList<Guid>> GetStoreIdsCarryingProductsAsync(CancellationToken cancellationToken = default)
    {
        var activeVariants = Variants.Where(variant => variant.IsActive && Products.Any(product => product.Id == variant.ProductId && product.IsActive)).Select(variant => variant.Id).ToHashSet();
        return Task.FromResult<IReadOnlyList<Guid>>(Inventory.Where(row => activeVariants.Contains(row.VariantId)).Select(row => row.StoreId).Distinct().ToArray());
    }

    /// <summary>Products for which the store has a stock row on at least one active variant.</summary>
    private HashSet<Guid> CarriedProductIds(Guid storeId)
    {
        var stocked = Inventory.Where(row => row.StoreId == storeId).Select(row => row.VariantId).ToHashSet();
        return Variants.Where(variant => variant.IsActive && stocked.Contains(variant.Id)).Select(variant => variant.ProductId).ToHashSet();
    }

    /// <summary>The default variant of a product (test and seeding helper).</summary>
    public ProductVariant DefaultVariantOf(Product product) => Variants.Single(variant => variant.ProductId == product.Id && variant.IsDefault);

    /// <summary>The stock row of a product's default variant in a store (test and seeding helper).</summary>
    public StoreVariantInventory StockOf(Store store, Product product)
    {
        var variantId = DefaultVariantOf(product).Id;
        return Inventory.Single(row => row.StoreId == store.Id && row.VariantId == variantId);
    }

    public Task<IReadOnlyList<StoreVariantInventory>> GetStoreVariantInventoryAsync(Guid storeId, IReadOnlyCollection<Guid> variantIds, CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlyList<StoreVariantInventory>>(Inventory.Where(row => row.StoreId == storeId && variantIds.Contains(row.VariantId)).ToArray());

    public Task<IReadOnlyList<ProductVariant>> GetVariantsAsync(IReadOnlyCollection<Guid> productIds, CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlyList<ProductVariant>>(Variants.Where(variant => productIds.Contains(variant.ProductId)).OrderBy(variant => variant.SortOrder).ThenBy(variant => variant.Price).ToArray());

    public Task<ProductVariant?> GetVariantAsync(Guid variantId, CancellationToken cancellationToken = default) => Task.FromResult(Variants.FirstOrDefault(variant => variant.Id == variantId));

    public Task<IReadOnlyList<ProductTranslation>> GetProductTranslationsAsync(IReadOnlyCollection<Guid> productIds, CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlyList<ProductTranslation>>(ProductTranslations.Where(translation => productIds.Contains(translation.ProductId)).ToArray());

    public Task<IReadOnlyList<CategoryTranslation>> GetCategoryTranslationsAsync(CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlyList<CategoryTranslation>>(CategoryTranslations.ToArray());

    public Task<Product?> GetProductAsync(Guid id, CancellationToken cancellationToken = default) => Task.FromResult(Products.FirstOrDefault(product => product.Id == id));

    public Task<IReadOnlyList<Store>> GetStoresAsync(CancellationToken cancellationToken = default) => Task.FromResult<IReadOnlyList<Store>>(Stores.ToArray());

    public Task<IReadOnlyList<Store>> GetScopedStoresAsync(Guid organizationId, IReadOnlySet<Guid> storeIds, bool isApplicationAdmin, CancellationToken cancellationToken = default) => Task.FromResult<IReadOnlyList<Store>>(Stores.Where(store => store.IsActive && store.OrganizationId == organizationId && (isApplicationAdmin || storeIds.Contains(store.Id))).ToArray());

    public Task<IReadOnlyList<StoreVariantInventory>> GetVariantInventoryAsync(CancellationToken cancellationToken = default) => Task.FromResult<IReadOnlyList<StoreVariantInventory>>(Inventory.ToArray());

    public Task<IReadOnlyList<Order>> GetOrdersAsync(CancellationToken cancellationToken = default) => Task.FromResult<IReadOnlyList<Order>>(Orders.ToArray());

    public Task<IReadOnlyList<Order>> GetScopedOrdersAsync(Guid organizationId, IReadOnlySet<Guid> storeIds, bool isApplicationAdmin, CancellationToken cancellationToken = default) => Task.FromResult<IReadOnlyList<Order>>(Orders.Where(order => Stores.Any(store => store.Id == order.StoreId && store.OrganizationId == organizationId && store.IsActive) && (isApplicationAdmin || storeIds.Contains(order.StoreId))).OrderByDescending(order => order.CreatedAt).ToArray());

    public Task<IReadOnlyList<AdminOrderResponse>> GetScopedOrderSummariesAsync(Guid organizationId, IReadOnlySet<Guid> storeIds, bool isApplicationAdmin, CancellationToken cancellationToken = default) => Task.FromResult<IReadOnlyList<AdminOrderResponse>>(Orders.Where(order => Stores.Any(store => store.Id == order.StoreId && store.OrganizationId == organizationId && store.IsActive) && (isApplicationAdmin || storeIds.Contains(order.StoreId))).OrderByDescending(order => order.CreatedAt).Select(order => new AdminOrderResponse(order.Id, order.OrderNumber, order.UserId, Users.FirstOrDefault(user => user.Id == order.UserId)?.DisplayName ?? "Unknown customer", order.StoreId, Stores.First(store => store.Id == order.StoreId).Name, order.TotalAmount, order.Status, order.CreatedAt)).ToArray());

    public Task<Order?> GetOrderAsync(Guid id, CancellationToken cancellationToken = default) => Task.FromResult(Orders.FirstOrDefault(order => order.Id == id));

    public Task<IReadOnlyList<Order>> GetCustomerOrdersAsync(Guid userId, Guid organizationId, CancellationToken cancellationToken = default) => Task.FromResult<IReadOnlyList<Order>>(Orders
        .Where(order => order.UserId == userId && Stores.Any(store => store.Id == order.StoreId && store.OrganizationId == organizationId))
        .ToArray());

    public Task<Order?> GetCustomerOrderAsync(Guid orderId, Guid userId, Guid organizationId, CancellationToken cancellationToken = default) => Task.FromResult(Orders
        .FirstOrDefault(order => order.Id == orderId && order.UserId == userId && Stores.Any(store => store.Id == order.StoreId && store.OrganizationId == organizationId)));

    public Task<IReadOnlyList<Notification>> GetNotificationsAsync(Guid customerId, bool unreadOnly, CancellationToken cancellationToken = default) => Task.FromResult<IReadOnlyList<Notification>>(Notifications
        .Where(notification => notification.CustomerId == customerId && (!unreadOnly || !notification.IsRead))
        .ToArray());

    public Task<bool> SetNotificationReadAsync(Guid notificationId, Guid customerId, bool isRead, CancellationToken cancellationToken = default)
    {
        var notification = Notifications.FirstOrDefault(item => item.Id == notificationId && item.CustomerId == customerId);
        if (notification is null)
        {
            return Task.FromResult(false);
        }

        notification.IsRead = isRead;
        return Task.FromResult(true);
    }

    public Task<UserContext?> GetUserContextAsync(string externalSubject, CancellationToken cancellationToken = default) => Task.FromResult<UserContext?>(Users
        .Where(user => (user.ExternalSubject == externalSubject || user.Id.ToString() == externalSubject) && user.IsActive)
        .Select(user => new UserContext(user.Id, user.OrganizationId, user.StoreId, user.Role, user.StaffCategory))
        .FirstOrDefault());

    public Task<Customer?> GetCustomerByUserIdAsync(Guid userId, CancellationToken cancellationToken = default) => Task.FromResult<Customer?>(Customers
        .FirstOrDefault(customer => customer.UserId == userId && customer.IsActive));

    public Task<Store?> GetStoreAsync(Guid id, CancellationToken cancellationToken = default) => Task.FromResult<Store?>(Stores
        .FirstOrDefault(store => store.Id == id));

    public Task<Cart?> GetCartAsync(Guid customerId, Guid storeId, CancellationToken cancellationToken = default) => Task.FromResult<Cart?>(Carts
        .FirstOrDefault(cart => cart.CustomerId == customerId && cart.StoreId == storeId));

    public Task AddCartAsync(Cart cart, CancellationToken cancellationToken = default)
    {
        Carts.Add(cart);
        return Task.CompletedTask;
    }

    public Task SaveCartAsync(Cart cart, CancellationToken cancellationToken = default) => Task.CompletedTask;

    public Task<Cart?> GetCurrentCartAsync(Guid customerId, CancellationToken cancellationToken = default)
    {
        lock (SyncRoot)
        {
            return Task.FromResult(Carts.Where(cart => cart.CustomerId == customerId).OrderByDescending(cart => cart.UpdatedAt).FirstOrDefault());
        }
    }

    public Task<bool> DeleteCartAsync(Guid customerId, Guid storeId, CancellationToken cancellationToken = default)
    {
        lock (SyncRoot)
        {
            return Task.FromResult(Carts.RemoveAll(cart => cart.CustomerId == customerId && cart.StoreId == storeId) > 0);
        }
    }

    /// <summary>Checkout keys already used: customer and key to what they produced.</summary>
    public List<CheckoutRequestRecord> CheckoutRequests { get; } = [];

    public Task<CheckoutCommitResult> TryCheckoutCartAsync(Guid customerId, Guid storeId, CheckoutCommit commit, CancellationToken cancellationToken = default)
    {
        lock (SyncRoot)
        {
            if (commit.IdempotencyKey is not null)
            {
                var record = CheckoutRequests.FirstOrDefault(item => item.CustomerId == customerId && item.IdempotencyKey == commit.IdempotencyKey);
                if (record is not null && record.CreatedAt < DateTime.UtcNow - TimeSpan.FromHours(24))
                {
                    CheckoutRequests.Remove(record);
                    record = null;
                }

                if (record is not null)
                {
                    return Task.FromResult(record.RequestHash == commit.RequestHash
                        ? new CheckoutCommitResult(CheckoutCommitStatus.Succeeded, MapOrder(Orders.Single(order => order.Id == record.OrderId)), Replayed: true)
                        : new CheckoutCommitResult(CheckoutCommitStatus.KeyReused));
                }
            }

            var cart = Carts.FirstOrDefault(item => item.CustomerId == customerId && item.StoreId == storeId);
            if (cart is null)
            {
                return Task.FromResult(new CheckoutCommitResult(CheckoutCommitStatus.CartNotFound));
            }

            if (cart.Items.Count == 0)
            {
                return Task.FromResult(new CheckoutCommitResult(CheckoutCommitStatus.CartEmpty));
            }

            var gone = new List<CheckoutIssue>();
            var notEnough = new List<CheckoutIssue>();
            var repriced = new List<CheckoutIssue>();
            var orderItems = new List<OrderItem>();
            foreach (var cartItem in cart.Items)
            {
                var product = Products.FirstOrDefault(item => item.Id == cartItem.ProductId);
                var variant = Variants.FirstOrDefault(item => item.Id == cartItem.VariantId && item.ProductId == cartItem.ProductId);
                if (product is null || !product.IsActive || variant is null || !variant.IsActive)
                {
                    gone.Add(new CheckoutIssue(cartItem.ProductId, cartItem.VariantId, cartItem.ProductNameSnapshot, cartItem.VariantLabelSnapshot, cartItem.Quantity));
                    continue;
                }

                var inventory = Inventory.FirstOrDefault(item => item.StoreId == storeId && item.VariantId == cartItem.VariantId);
                if (inventory is null || inventory.AvailableQuantity < cartItem.Quantity)
                {
                    notEnough.Add(new CheckoutIssue(cartItem.ProductId, cartItem.VariantId, product.Name, variant.Label, cartItem.Quantity, Available: inventory?.AvailableQuantity ?? 0));
                    continue;
                }

                if (variant.Price != cartItem.UnitPriceSnapshot)
                {
                    repriced.Add(new CheckoutIssue(cartItem.ProductId, cartItem.VariantId, product.Name, variant.Label, cartItem.Quantity, OldPrice: cartItem.UnitPriceSnapshot, NewPrice: variant.Price));
                    continue;
                }

                orderItems.Add(new OrderItem
                {
                    ProductId = product.Id,
                    VariantId = variant.Id,
                    ProductNameSnapshot = product.Name,
                    VariantLabelSnapshot = variant.Label,
                    UnitPrice = variant.Price,
                    UnitMrpSnapshot = variant.Mrp,
                    Quantity = cartItem.Quantity
                });
            }

            if (gone.Count > 0 || notEnough.Count > 0 || repriced.Count > 0)
            {
                return Task.FromResult(gone.Count > 0 ? new CheckoutCommitResult(CheckoutCommitStatus.ProductUnavailable, Issues: gone)
                    : notEnough.Count > 0 ? new CheckoutCommitResult(CheckoutCommitStatus.InventoryConflict, Issues: notEnough)
                    : new CheckoutCommitResult(CheckoutCommitStatus.PriceChanged, Issues: repriced));
            }

            foreach (var cartItem in cart.Items)
            {
                Inventory.Single(item => item.StoreId == storeId && item.VariantId == cartItem.VariantId).AvailableQuantity -= cartItem.Quantity;
            }

            var price = PricingCalculator.Compute(commit.Pricing, orderItems.Sum(item => item.TotalPrice));
            var order = new Order
            {
                OrderNumber = $"ORD-{DateTime.UtcNow:yyyyMMddHHmmss}-{Guid.NewGuid():N}"[..32],
                UserId = Customers.Single(customer => customer.Id == customerId).UserId,
                StoreId = storeId,
                DeliveryAddress = commit.Delivery.Address,
                Latitude = commit.Delivery.Latitude,
                Longitude = commit.Delivery.Longitude,
                DeliveryAddressId = commit.Delivery.AddressId,
                ReceiverName = commit.Delivery.ReceiverName,
                ReceiverPhone = commit.Delivery.ReceiverPhone,
                EstimatedDeliveryMinutes = commit.EstimatedDeliveryMinutes,
                SubtotalAmount = price.Subtotal,
                DeliveryFee = price.DeliveryFee,
                HandlingFee = price.HandlingFee,
                TotalAmount = price.Total,
                PaymentMethod = PaymentMethods.CashOnDelivery,
                Items = orderItems
            };
            order.StatusHistory.Add(new OrderStatusHistory { Status = OrderStatus.Pending });
            Orders.Add(order);
            if (commit.IdempotencyKey is not null)
            {
                CheckoutRequests.Add(new CheckoutRequestRecord { CustomerId = customerId, IdempotencyKey = commit.IdempotencyKey, RequestHash = commit.RequestHash!, OrderId = order.Id });
            }

            cart.Items.Clear();
            cart.UpdatedAt = DateTime.UtcNow;
            return Task.FromResult(new CheckoutCommitResult(CheckoutCommitStatus.Succeeded, MapOrder(order)));
        }
    }

    public Task<OrderLifecycleResult> TryTransitionOrderAsync(Guid orderId, Guid organizationId, Guid? storeId, OrderStatus targetStatus, CancellationToken cancellationToken = default)
    {
        lock (SyncRoot)
        {
            var order = Orders.FirstOrDefault(currentOrder => currentOrder.Id == orderId &&
                (!storeId.HasValue || currentOrder.StoreId == storeId.Value));
            var store = order is null
                ? null
                : Stores.FirstOrDefault(currentStore => currentStore.Id == order.StoreId && currentStore.OrganizationId == organizationId);
            if (order is null || store is null)
            {
                return Task.FromResult(new OrderLifecycleResult(OrderLifecycleStatus.NotFound));
            }

            if (!OrderStatusTransitions.IsValid(order.Status, targetStatus))
            {
                return Task.FromResult(new OrderLifecycleResult(OrderLifecycleStatus.InvalidTransition));
            }

            order.Status = targetStatus;
            order.StatusHistory.Add(new OrderStatusHistory { Status = targetStatus });
            var customer = Customers.FirstOrDefault(customer => customer.UserId == order.UserId);
            if (customer is not null)
            {
                Notifications.Add(CreateStatusNotification(customer.Id, order.Id, targetStatus));
            }
            return Task.FromResult(new OrderLifecycleResult(OrderLifecycleStatus.Succeeded, MapOrder(order)));
        }
    }

    public Task<bool> TryCreateOrderAsync(Order order, IReadOnlyCollection<InventoryAdjustment> inventoryAdjustments, CancellationToken cancellationToken = default)
    {
        lock (SyncRoot)
        {
            foreach (var adjustment in inventoryAdjustments)
            {
                var stock = Inventory.FirstOrDefault(item => item.StoreId == adjustment.StoreId && item.VariantId == adjustment.VariantId);
                if (stock is null || stock.AvailableQuantity < adjustment.Quantity)
                {
                    return Task.FromResult(false);
                }
            }

            foreach (var adjustment in inventoryAdjustments)
            {
                Inventory.First(item => item.StoreId == adjustment.StoreId && item.VariantId == adjustment.VariantId).AvailableQuantity -= adjustment.Quantity;
            }

            Orders.Add(order);
            return Task.FromResult(true);
        }
    }

    private static OrderResponse MapOrder(Order order) => new(
        order.Id,
        order.OrderNumber,
        order.UserId,
        order.StoreId,
        order.TotalAmount,
        order.Status,
        order.DeliveryAddress,
        order.Latitude,
        order.Longitude,
        order.CreatedAt,
        order.Items.Select(item => new OrderItemResponse(item.ProductId, item.ProductNameSnapshot, item.UnitPrice, item.Quantity, item.TotalPrice, item.VariantId, item.VariantLabelSnapshot)).ToArray(),
        order.StatusHistory.Select(history => new OrderStatusHistoryResponse(history.Status, history.ChangedAt)).ToArray(),
        order.SubtotalAmount,
        order.DeliveryFee,
        order.HandlingFee,
        order.PaymentMethod,
        order.ReceiverName,
        order.ReceiverPhone,
        EstimatedDeliveryMinutes: order.EstimatedDeliveryMinutes);

    public Task<int> GetUnreadNotificationCountAsync(Guid customerId, CancellationToken cancellationToken = default)
    {
        lock (SyncRoot)
        {
            return Task.FromResult(Notifications.Count(notification => notification.CustomerId == customerId && !notification.IsRead));
        }
    }

    public Task<int> MarkAllNotificationsReadAsync(Guid customerId, CancellationToken cancellationToken = default)
    {
        lock (SyncRoot)
        {
            var unread = Notifications.Where(notification => notification.CustomerId == customerId && !notification.IsRead).ToList();
            unread.ForEach(notification => notification.IsRead = true);
            return Task.FromResult(unread.Count);
        }
    }

    public Task<CancelCommitResult> TryCancelCustomerOrderAsync(Guid orderId, Guid userId, Guid organizationId, CancellationToken cancellationToken = default)
    {
        lock (SyncRoot)
        {
            var order = Orders.FirstOrDefault(item => item.Id == orderId && item.UserId == userId && Stores.Any(store => store.Id == item.StoreId && store.OrganizationId == organizationId));
            if (order is null)
            {
                return Task.FromResult(new CancelCommitResult(CancelCommitStatus.NotFound));
            }

            if (order.Status == OrderStatus.Cancelled)
            {
                return Task.FromResult(new CancelCommitResult(CancelCommitStatus.AlreadyCancelled, MapOrder(order)));
            }

            if (order.Status != OrderStatus.Pending)
            {
                return Task.FromResult(new CancelCommitResult(CancelCommitStatus.NotCancellable, CurrentStatus: order.Status));
            }

            order.Status = OrderStatus.Cancelled;
            order.StatusHistory.Add(new OrderStatusHistory { Status = OrderStatus.Cancelled });
            foreach (var line in order.Items.Where(item => item.VariantId.HasValue).GroupBy(item => item.VariantId!.Value))
            {
                var row = Inventory.FirstOrDefault(item => item.StoreId == order.StoreId && item.VariantId == line.Key);
                if (row is not null)
                {
                    row.AvailableQuantity += line.Sum(item => item.Quantity);
                }
            }

            var customer = Customers.FirstOrDefault(item => item.UserId == order.UserId);
            if (customer is not null)
            {
                Notifications.Add(CreateStatusNotification(customer.Id, order.Id, OrderStatus.Cancelled));
            }

            return Task.FromResult(new CancelCommitResult(CancelCommitStatus.Cancelled, MapOrder(order)));
        }
    }

    private static Notification CreateStatusNotification(Guid customerId, Guid orderId, OrderStatus status) => new()
    {
        CustomerId = customerId,
        OrderId = orderId,
        Type = "OrderStatusChanged",
        Title = status == OrderStatus.Cancelled ? "Order cancelled" : "Order status updated",
        Message = status == OrderStatus.Cancelled ? "Your order was cancelled." : $"Your order is now {status}.",
        IsRead = false
    };

    public static InMemoryCommerceStore CreateSeeded()
    {
        var store = new InMemoryCommerceStore();
        var organization = new Organization { Name = "QuickCart Demo Retailer" };
        store.Organizations.Add(organization);
        var user = new User
        {
            ExternalSubject = "quickcart-demo-user",
            DisplayName = "QuickCart Demo User",
            OrganizationId = organization.Id,
            Organization = organization
        };
        store.Users.Add(user);
        var customer = new Customer { UserId = user.Id, User = user };
        user.Customer = customer;
        store.Customers.Add(customer);
        var groceries = new Category { Name = "Groceries" };
        var vegetables = new Category { Name = "Vegetables" };
        var fruits = new Category { Name = "Fruits" };
        var dairy = new Category { Name = "Dairy" };
        store.Categories.AddRange([groceries, vegetables, fruits, dairy]);

        var products = new[]
        {
            new Product { Sku = "VEG-TOM", Name = "Tomato", Description = "Fresh red tomatoes", Price = 45, UnitOfMeasure = "1 kg", CategoryId = vegetables.Id, ImageUrl = "https://images.unsplash.com/photo-1546094096-0df4bcaaa337?w=640" },
            new Product { Sku = "VEG-POT", Name = "Potato", Description = "Everyday cooking potatoes", Price = 38, UnitOfMeasure = "1 kg", CategoryId = vegetables.Id, ImageUrl = "https://images.unsplash.com/photo-1518977676601-b53f82aba655?w=640" },
            new Product { Sku = "DAI-MILK", Name = "Farm Milk", Description = "Pasteurized full cream milk", Price = 34, UnitOfMeasure = "500 ml", CategoryId = dairy.Id, ImageUrl = "https://images.unsplash.com/photo-1550583724-b2692b85b150?w=640" },
            new Product { Sku = "FRT-APL", Name = "Royal Gala Apples", Description = "Crisp and naturally sweet", Price = 149, UnitOfMeasure = "1 kg", CategoryId = fruits.Id, ImageUrl = "https://images.unsplash.com/photo-1560806887-1e4cd0b6cbd6?w=640" },
            new Product { Sku = "GRO-RICE", Name = "Daily Rice", Description = "Long grain rice for every meal", Price = 89, UnitOfMeasure = "1 kg", CategoryId = groceries.Id, ImageUrl = "https://images.unsplash.com/photo-1586201375761-83865001e31c?w=640" }
        };
        store.Products.AddRange(products);

        store.Stores.AddRange([
            new Store { Name = "Harbor Point Dark Store", Address = "12 Marine Drive", Latitude = 19.076, Longitude = 72.8777, ServiceRadiusKm = 8, OrganizationId = organization.Id, Organization = organization },
            new Store { Name = "Cedar Market Hub", Address = "44 Cedar Avenue", Latitude = 19.102, Longitude = 72.916, ServiceRadiusKm = 7, OrganizationId = organization.Id, Organization = organization },
            new Store { Name = "North Star Fulfillment", Address = "8 Station Road", Latitude = 19.045, Longitude = 72.899, ServiceRadiusKm = 9, OrganizationId = organization.Id, Organization = organization }
        ]);

        // Every product has exactly one default variant, copying its own price, MRP and unit.
        foreach (var product in products)
        {
            store.Variants.Add(new ProductVariant { ProductId = product.Id, Sku = product.Sku, Label = product.UnitOfMeasure, Price = product.Price, Mrp = product.Mrp, SortOrder = 0, IsDefault = true });
        }

        var quantities = new[] { 24, 8, 0, 16, 5 };
        foreach (var currentStore in store.Stores)
        {
            for (var index = 0; index < products.Length; index++)
            {
                store.Inventory.Add(new StoreVariantInventory { StoreId = currentStore.Id, VariantId = store.DefaultVariantOf(products[index]).Id, AvailableQuantity = Math.Max(0, quantities[index] + store.Stores.IndexOf(currentStore) * 4) });
            }
        }

        return store;
    }
}
