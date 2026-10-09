using QuickCommerce.Application.Services;
using QuickCommerce.Application.Interfaces;
using QuickCommerce.Application.DTOs;
using QuickCommerce.Domain;

namespace QuickCommerce.Infrastructure;

public sealed class InMemoryCommerceStore : ICommerceStore, IPaymentStore, ICampaignStore
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
    public List<Payment> Payments { get; } = [];
    public List<PaymentEvent> PaymentEvents { get; } = [];
    public List<Order> Orders { get; } = [];
    public object SyncRoot { get; } = new();

    public Task<AdminDashboardSnapshot> GetAdminDashboardAsync(Guid organizationId, IReadOnlySet<Guid> storeIds, bool isApplicationAdmin, CancellationToken cancellationToken = default)
    {
        var scopedStores = Stores.Where(store => store.IsActive && store.OrganizationId == organizationId && (isApplicationAdmin || storeIds.Contains(store.Id))).ToArray();
        var scopedStoreIds = scopedStores.Select(store => store.Id).ToHashSet();
        var scopedOrders = Orders.Where(order => order.Status != OrderStatus.AwaitingPayment && scopedStoreIds.Contains(order.StoreId)).OrderByDescending(order => order.CreatedAt).ToArray();
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

    public Task<IReadOnlyList<Order>> GetOrdersAsync(CancellationToken cancellationToken = default) => Task.FromResult<IReadOnlyList<Order>>(Orders.Where(order => order.Status != OrderStatus.AwaitingPayment).ToArray());

    public Task<IReadOnlyList<Order>> GetScopedOrdersAsync(Guid organizationId, IReadOnlySet<Guid> storeIds, bool isApplicationAdmin, CancellationToken cancellationToken = default) => Task.FromResult<IReadOnlyList<Order>>(Orders.Where(order => order.Status != OrderStatus.AwaitingPayment && Stores.Any(store => store.Id == order.StoreId && store.OrganizationId == organizationId && store.IsActive) && (isApplicationAdmin || storeIds.Contains(order.StoreId))).OrderByDescending(order => order.CreatedAt).ToArray());

    public Task<IReadOnlyList<AdminOrderResponse>> GetScopedOrderSummariesAsync(Guid organizationId, IReadOnlySet<Guid> storeIds, bool isApplicationAdmin, CancellationToken cancellationToken = default) => Task.FromResult<IReadOnlyList<AdminOrderResponse>>(Orders.Where(order => order.Status != OrderStatus.AwaitingPayment && Stores.Any(store => store.Id == order.StoreId && store.OrganizationId == organizationId && store.IsActive) && (isApplicationAdmin || storeIds.Contains(order.StoreId))).OrderByDescending(order => order.CreatedAt).Select(order => new AdminOrderResponse(order.Id, order.OrderNumber, order.UserId, Users.FirstOrDefault(user => user.Id == order.UserId)?.DisplayName ?? "Unknown customer", order.StoreId, Stores.First(store => store.Id == order.StoreId).Name, order.TotalAmount, order.Status, order.CreatedAt)).ToArray());

    public Task<Order?> GetOrderAsync(Guid id, CancellationToken cancellationToken = default) => Task.FromResult(Orders.FirstOrDefault(order => order.Id == id && order.Status != OrderStatus.AwaitingPayment));

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

    public Task SetOffersEnabledAsync(Guid customerId, bool enabled, CancellationToken cancellationToken = default)
    {
        lock (SyncRoot)
        {
            var customer = Customers.FirstOrDefault(item => item.Id == customerId);
            if (customer is not null)
            {
                customer.OffersEnabled = enabled;
            }
        }

        return Task.CompletedTask;
    }

    public List<Campaign> Campaigns { get; } = [];

    public Task<IReadOnlyList<Campaign>> ListCampaignsAsync(CancellationToken cancellationToken = default)
    {
        lock (SyncRoot)
        {
            return Task.FromResult<IReadOnlyList<Campaign>>(Campaigns.ToArray());
        }
    }

    public Task AddCampaignAsync(Campaign campaign, CancellationToken cancellationToken = default)
    {
        lock (SyncRoot)
        {
            Campaigns.Add(campaign);
        }

        return Task.CompletedTask;
    }

    public Task<Campaign?> GetCampaignAsync(Guid id, CancellationToken cancellationToken = default)
    {
        lock (SyncRoot)
        {
            return Task.FromResult(Campaigns.FirstOrDefault(campaign => campaign.Id == id));
        }
    }

    public Task<Campaign?> TryCancelCampaignAsync(Guid id, CancellationToken cancellationToken = default)
    {
        lock (SyncRoot)
        {
            var campaign = Campaigns.FirstOrDefault(item => item.Id == id);
            if (campaign is not null && campaign.PublishedAt is null)
            {
                campaign.IsActive = false;
            }

            return Task.FromResult(campaign);
        }
    }

    public Task<int> PublishDueCampaignsAsync(DateTime now, CancellationToken cancellationToken = default)
    {
        lock (SyncRoot)
        {
            var sent = 0;
            foreach (var campaign in Campaigns.Where(item => item.IsActive && item.PublishedAt is null && item.StartsAt <= now).ToArray())
            {
                campaign.PublishedAt = now;
                var recipients = Customers.Where(customer => customer.IsActive && customer.OffersEnabled).ToArray();
                foreach (var customer in recipients)
                {
                    Notifications.Add(new Notification { CustomerId = customer.Id, Type = NotificationTypes.Offer, Category = NotificationCategories.Offer, Title = campaign.TitleEn, Message = campaign.BodyEn, CampaignId = campaign.Id, Campaign = campaign });
                }

                campaign.RecipientCount = recipients.Length;
                sent++;
            }

            return Task.FromResult(sent);
        }
    }

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
                OrderNumber = NextOrderNumber(storeId),
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
                PaymentMethod = commit.PaymentMethod,
                Status = commit.PaymentMethod == PaymentMethods.Online ? OrderStatus.AwaitingPayment : OrderStatus.Pending,
                PaymentStatus = commit.PaymentMethod == PaymentMethods.Online ? PaymentState.Created : PaymentState.NotRequired,
                PaymentExpiresAt = commit.PaymentMethod == PaymentMethods.Online ? commit.PaymentExpiresAt : null,
                Items = orderItems
            };
            // An online order waits for its payment, holding its items; the shop sees it only once it is paid.
            order.StatusHistory.Add(new OrderStatusHistory { Status = order.Status });
            Orders.Add(order);
            if (order.Status == OrderStatus.Pending)
            {
                Notifications.Add(NotificationCatalog.Create(customerId, order.Id, NotificationTypes.OrderPlaced, order.OrderNumber));
            }
            if (order.Status == OrderStatus.AwaitingPayment)
            {
                Payments.Add(new Payment { OrderId = order.Id, AmountPaise = PaymentTransitions.ToPaise(order.TotalAmount) });
            }

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
            if (targetStatus == OrderStatus.Rejected)
            {
                EndPayment(order, wasAwaitingPayment: false);
                foreach (var line in order.Items.Where(item => item.VariantId.HasValue).GroupBy(item => item.VariantId!.Value))
                {
                    var row = Inventory.FirstOrDefault(item => item.StoreId == order.StoreId && item.VariantId == line.Key);
                    if (row is not null)
                    {
                        row.AvailableQuantity += line.Sum(item => item.Quantity);
                    }
                }
            }

            var customer = Customers.FirstOrDefault(customer => customer.UserId == order.UserId);
            if (customer is not null)
            {
                Notifications.Add(NotificationCatalog.ForStatus(customer.Id, order, targetStatus));
            }
            return Task.FromResult(new OrderLifecycleResult(OrderLifecycleStatus.Succeeded, MapOrder(order)));
        }
    }

    private readonly Dictionary<(Guid StoreId, DateOnly Day), int> orderCounters = [];

    /// <summary>The next order number for the store today, like the SQL store (called while the lock is held).</summary>
    private string NextOrderNumber(Guid storeId)
    {
        var day = OrderNumbers.DayOf(DateTime.UtcNow);
        orderCounters[(storeId, day)] = orderCounters.GetValueOrDefault((storeId, day)) + 1;
        return OrderNumbers.Format(OrderNumbers.CodeOf(Stores.Single(item => item.Id == storeId)), day, orderCounters[(storeId, day)]);
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

            order.OrderNumber = NextOrderNumber(order.StoreId);
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
        EstimatedDeliveryMinutes: order.EstimatedDeliveryMinutes,
        PaymentStatus: order.PaymentStatus,
        PaymentExpiresAt: order.PaymentExpiresAt);

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

            if (order.Status is not (OrderStatus.Pending or OrderStatus.AwaitingPayment))
            {
                return Task.FromResult(new CancelCommitResult(CancelCommitStatus.NotCancellable, CurrentStatus: order.Status));
            }

            var wasAwaitingPayment = order.Status == OrderStatus.AwaitingPayment;
            order.Status = OrderStatus.Cancelled;
            order.StatusHistory.Add(new OrderStatusHistory { Status = OrderStatus.Cancelled });
            EndPayment(order, wasAwaitingPayment);
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
                Notifications.Add(NotificationCatalog.ForStatus(customer.Id, order, OrderStatus.Cancelled));
            }

            return Task.FromResult(new CancelCommitResult(CancelCommitStatus.Cancelled, MapOrder(order)));
        }
    }

    // ---------------- payments ----------------

    private void EndPayment(Order order, bool wasAwaitingPayment)
    {
        var payment = Payments.FirstOrDefault(item => item.OrderId == order.Id);
        var now = DateTime.UtcNow;
        if (wasAwaitingPayment)
        {
            order.PaymentStatus = PaymentState.Failed;
            if (payment is not null && payment.Status is PaymentState.Created or PaymentState.Failed)
            {
                payment.Status = PaymentState.Failed;
                payment.FailureReason = "Cancelled";
                payment.UpdatedAt = now;
            }

            return;
        }

        PaymentTransitions.QueueRefundIfPaid(order, payment, now);
    }

    private void ReturnStock(Order order)
    {
        foreach (var line in order.Items.Where(item => item.VariantId.HasValue).GroupBy(item => item.VariantId!.Value))
        {
            var row = Inventory.FirstOrDefault(item => item.StoreId == order.StoreId && item.VariantId == line.Key);
            if (row is not null)
            {
                row.AvailableQuantity += line.Sum(item => item.Quantity);
            }
        }
    }

    private void NotePayment(Order order, NoteText? note)
    {
        var customer = Customers.FirstOrDefault(item => item.UserId == order.UserId);
        if (note is not null && customer is not null)
        {
            Notifications.Add(NotificationCatalog.From(customer.Id, order.Id, note));
        }
    }

    public Task<PaymentOrderView?> GetOrderAsync(Guid orderId, Guid userId, Guid organizationId, CancellationToken cancellationToken = default)
    {
        lock (SyncRoot)
        {
            var order = Orders.FirstOrDefault(item => item.Id == orderId && item.UserId == userId && Stores.Any(store => store.Id == item.StoreId && store.OrganizationId == organizationId));
            return Task.FromResult(order is null ? null : new PaymentOrderView(order.Id, order.OrderNumber, order.Status, order.PaymentMethod, order.PaymentStatus, order.TotalAmount, order.PaymentExpiresAt));
        }
    }

    public Task<Payment?> GetPaymentAsync(Guid orderId, CancellationToken cancellationToken = default)
    {
        lock (SyncRoot)
        {
            return Task.FromResult(Payments.FirstOrDefault(item => item.OrderId == orderId));
        }
    }

    public Task<Payment?> BeginAttemptAsync(Guid orderId, DateTime now, CancellationToken cancellationToken = default)
    {
        lock (SyncRoot)
        {
            var order = Orders.FirstOrDefault(item => item.Id == orderId);
            var payment = Payments.FirstOrDefault(item => item.OrderId == orderId);
            if (order is null || payment is null || order.Status != OrderStatus.AwaitingPayment)
            {
                return Task.FromResult<Payment?>(null);
            }

            payment.Attempts++;
            payment.UpdatedAt = now;
            if (payment.Status == PaymentState.Failed)
            {
                payment.Status = PaymentState.Created;
                order.PaymentStatus = PaymentState.Created;
            }

            return Task.FromResult<Payment?>(payment);
        }
    }

    public Task<bool> AttachProviderOrderAsync(Guid paymentId, string providerOrderId, CancellationToken cancellationToken = default)
    {
        lock (SyncRoot)
        {
            var payment = Payments.First(item => item.Id == paymentId);
            if (payment.ProviderOrderId is not null)
            {
                return Task.FromResult(false);
            }

            payment.ProviderOrderId = providerOrderId;
            return Task.FromResult(true);
        }
    }

    public Task<CapturedOutcome> ApplyCapturedAsync(string providerOrderId, string providerPaymentId, long paidPaise, DateTime now, CancellationToken cancellationToken = default)
    {
        lock (SyncRoot)
        {
            var payment = Payments.FirstOrDefault(item => item.ProviderOrderId == providerOrderId);
            if (payment is null)
            {
                return Task.FromResult(CapturedOutcome.UnknownPayment);
            }

            var order = Orders.Single(item => item.Id == payment.OrderId);
            var result = PaymentTransitions.ApplyCaptured(order, payment, providerPaymentId, paidPaise, now);
            NotePayment(order, result.Note);
            return Task.FromResult(result.Outcome);
        }
    }

    public Task<bool> ApplyFailedAsync(string providerOrderId, string? providerPaymentId, string reason, DateTime now, CancellationToken cancellationToken = default)
    {
        lock (SyncRoot)
        {
            var payment = Payments.FirstOrDefault(item => item.ProviderOrderId == providerOrderId);
            if (payment is null)
            {
                return Task.FromResult(false);
            }

            PaymentTransitions.ApplyFailed(Orders.Single(item => item.Id == payment.OrderId), payment, providerPaymentId, reason, now);
            return Task.FromResult(true);
        }
    }

    public Task<IReadOnlyList<Guid>> ReleaseExpiredAsync(DateTime now, int max, CancellationToken cancellationToken = default)
    {
        lock (SyncRoot)
        {
            var released = new List<Guid>();
            foreach (var order in Orders.Where(item => item.Status == OrderStatus.AwaitingPayment && item.PaymentExpiresAt <= now).Take(max).ToArray())
            {
                var note = PaymentTransitions.ExpireHold(order, Payments.FirstOrDefault(item => item.OrderId == order.Id), now);
                ReturnStock(order);
                NotePayment(order, note);
                released.Add(order.Id);
            }

            return Task.FromResult<IReadOnlyList<Guid>>(released);
        }
    }

    public Task<IReadOnlyList<Payment>> GetRefundsToStartAsync(int max, CancellationToken cancellationToken = default)
    {
        lock (SyncRoot)
        {
            return Task.FromResult<IReadOnlyList<Payment>>(Payments.Where(item => item.Status == PaymentState.Refunding && item.RefundId is null).Take(max).ToArray());
        }
    }

    public Task SetRefundStartedAsync(Guid paymentId, string refundId, bool processed, DateTime now, CancellationToken cancellationToken = default)
    {
        lock (SyncRoot)
        {
            var payment = Payments.First(item => item.Id == paymentId);
            var order = Orders.Single(item => item.Id == payment.OrderId);
            payment.RefundId = refundId;
            payment.UpdatedAt = now;
            if (processed)
            {
                NotePayment(order, PaymentTransitions.ApplyRefundResult(order, payment, refundId, true, now));
            }

            return Task.CompletedTask;
        }
    }

    public Task SetRefundFailedAsync(Guid paymentId, string reason, bool permanent, DateTime now, CancellationToken cancellationToken = default)
    {
        lock (SyncRoot)
        {
            var payment = Payments.First(item => item.Id == paymentId);
            payment.FailureReason = reason.Length <= 200 ? reason : reason[..200];
            payment.UpdatedAt = now;
            if (permanent)
            {
                payment.Status = PaymentState.RefundFailed;
                Orders.Single(item => item.Id == payment.OrderId).PaymentStatus = PaymentState.RefundFailed;
            }

            return Task.CompletedTask;
        }
    }

    public Task<bool> ApplyRefundResultAsync(string providerPaymentId, string refundId, bool processed, DateTime now, CancellationToken cancellationToken = default)
    {
        lock (SyncRoot)
        {
            var payment = Payments.FirstOrDefault(item => item.ProviderPaymentId == providerPaymentId);
            if (payment is null)
            {
                return Task.FromResult(false);
            }

            var order = Orders.Single(item => item.Id == payment.OrderId);
            NotePayment(order, PaymentTransitions.ApplyRefundResult(order, payment, refundId, processed, now));
            return Task.FromResult(true);
        }
    }

    public Task<IReadOnlyList<Payment>> GetUnsettledAsync(DateTime olderThan, int max, CancellationToken cancellationToken = default)
    {
        lock (SyncRoot)
        {
            return Task.FromResult<IReadOnlyList<Payment>>(Payments
                .Where(item => item.UpdatedAt < olderThan && item.CreatedAt > olderThan.AddDays(-7) &&
                    ((item.Status is PaymentState.Created or PaymentState.Failed && item.ProviderOrderId is not null) || (item.Status == PaymentState.Refunding && item.RefundId is not null)))
                .Take(max).ToArray());
        }
    }

    public Task<bool> TryRecordEventAsync(string provider, string eventId, string type, DateTime now, CancellationToken cancellationToken = default)
    {
        lock (SyncRoot)
        {
            if (PaymentEvents.Any(item => item.Provider == provider && item.EventId == eventId))
            {
                return Task.FromResult(false);
            }

            PaymentEvents.Add(new PaymentEvent { Provider = provider, EventId = eventId, Type = type, ReceivedAt = now });
            return Task.FromResult(true);
        }
    }

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
            new Store { Code = "HBR", Name = "Harbor Point Dark Store", Address = "12 Marine Drive", Latitude = 19.076, Longitude = 72.8777, ServiceRadiusKm = 8, OrganizationId = organization.Id, Organization = organization },
            new Store { Code = "CDR", Name = "Cedar Market Hub", Address = "44 Cedar Avenue", Latitude = 19.102, Longitude = 72.916, ServiceRadiusKm = 7, OrganizationId = organization.Id, Organization = organization },
            new Store { Code = "NSF", Name = "North Star Fulfillment", Address = "8 Station Road", Latitude = 19.045, Longitude = 72.899, ServiceRadiusKm = 9, OrganizationId = organization.Id, Organization = organization }
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
