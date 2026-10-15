using QuickCommerce.Application.Services;
using QuickCommerce.Application.Interfaces;
using QuickCommerce.Application.DTOs;
using QuickCommerce.Domain;

namespace QuickCommerce.Infrastructure;

public sealed class InMemoryCommerceStore : ICommerceStore, IPaymentStore, ICampaignStore, IOfferDiscountLookup, ICampaignPaymentStore, IOfferFeeStore, IPlacementStore, IDeliveryStore, ICatalogAdminStore
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

    public Task<IReadOnlyList<AdminOrderResponse>> GetScopedOrderSummariesAsync(Guid organizationId, IReadOnlySet<Guid> storeIds, bool isApplicationAdmin, CancellationToken cancellationToken = default) => Task.FromResult<IReadOnlyList<AdminOrderResponse>>(Orders.Where(order => order.Status != OrderStatus.AwaitingPayment && Stores.Any(store => store.Id == order.StoreId && store.OrganizationId == organizationId && store.IsActive) && (isApplicationAdmin || storeIds.Contains(order.StoreId))).OrderBy(order => OrderAttention.Rank(order.Status)).ThenByDescending(order => order.CreatedAt).Select(order => new AdminOrderResponse(order.Id, order.OrderNumber, order.UserId, Users.FirstOrDefault(user => user.Id == order.UserId)?.DisplayName ?? "Unknown customer", order.StoreId, Stores.First(store => store.Id == order.StoreId).Name, order.TotalAmount, order.Status, order.CreatedAt, order.DeliveryPartnerId, order.DeliveryPartnerId.HasValue ? Users.FirstOrDefault(user => user.Id == order.DeliveryPartnerId)?.DisplayName : null)).ToArray());

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
    public List<CampaignPayment> CampaignPayments { get; } = [];
    public List<PlacementBid> PlacementBids { get; } = [];
    private PlacementRules? placementRules;

    // ---- paid placement ----

    public Task<PlacementRules> GetRulesAsync(CancellationToken cancellationToken = default)
    {
        lock (SyncRoot)
        {
            var rules = placementRules ?? new PlacementRules();
            return Task.FromResult(new PlacementRules { Id = rules.Id, Enabled = rules.Enabled, MinBidPaise = rules.MinBidPaise, MinStepPaise = rules.MinStepPaise, MaxWeeksAhead = rules.MaxWeeksAhead, UpdatedAt = rules.UpdatedAt, UpdatedBy = rules.UpdatedBy });
        }
    }

    public Task SetRulesAsync(PlacementRules rules, CancellationToken cancellationToken = default)
    {
        lock (SyncRoot)
        {
            placementRules = rules;
        }

        return Task.CompletedTask;
    }

    Task<Store?> IPlacementStore.GetStoreAsync(Guid storeId, Guid organizationId, CancellationToken cancellationToken)
    {
        lock (SyncRoot)
        {
            return Task.FromResult(Stores.FirstOrDefault(store => store.Id == storeId && store.OrganizationId == organizationId && store.IsActive));
        }
    }

    public Task<PlacementBid?> GetLeadingAsync(string pincode, DateOnly weekStart, CancellationToken cancellationToken = default)
    {
        lock (SyncRoot)
        {
            return Task.FromResult(PlacementBids.FirstOrDefault(bid => bid.Pincode == pincode && bid.WeekStart == weekStart && bid.Status == PlacementBidStatus.Leading));
        }
    }

    public async Task<Guid?> GetLeadingStoreAsync(string pincode, DateTime nowUtc, CancellationToken cancellationToken = default)
    {
        if (!PlacementWeeks.IsPincode(pincode) || !(await GetRulesAsync(cancellationToken)).Enabled)
        {
            return null;
        }

        return (await GetLeadingAsync(pincode, PlacementWeeks.WeekStartOf(nowUtc), cancellationToken))?.StoreId;
    }

    public Task<PlacementBid?> GetBidAsync(Guid bidId, CancellationToken cancellationToken = default)
    {
        lock (SyncRoot)
        {
            return Task.FromResult(PlacementBids.FirstOrDefault(bid => bid.Id == bidId));
        }
    }

    public Task<IReadOnlyList<PlacementBid>> ListBidsAsync(Guid? storeId, int max, CancellationToken cancellationToken = default)
    {
        lock (SyncRoot)
        {
            return Task.FromResult<IReadOnlyList<PlacementBid>>(PlacementBids.Where(bid => storeId is null || bid.StoreId == storeId).OrderByDescending(bid => bid.CreatedAt).Take(max).ToArray());
        }
    }

    public Task<int> CountOpenBidsAsync(Guid storeId, CancellationToken cancellationToken = default)
    {
        lock (SyncRoot)
        {
            return Task.FromResult(PlacementBids.Count(bid => bid.StoreId == storeId && bid.Status == PlacementBidStatus.AwaitingPayment));
        }
    }

    public Task AddBidAsync(PlacementBid bid, CancellationToken cancellationToken = default)
    {
        lock (SyncRoot)
        {
            bid.Store = Stores.First(store => store.Id == bid.StoreId);
            PlacementBids.Add(bid);
        }

        return Task.CompletedTask;
    }

    public Task<PlacementBid?> CancelBidAsync(Guid bidId, DateTime now, CancellationToken cancellationToken = default)
    {
        lock (SyncRoot)
        {
            var bid = PlacementBids.FirstOrDefault(item => item.Id == bidId);
            if (bid is not null)
            {
                PlacementTransitions.ApplyCancelled(bid, now);
            }

            return Task.FromResult(bid);
        }
    }

    Task<PlacementBid?> IPlacementStore.BeginAttemptAsync(Guid bidId, DateTime now, CancellationToken cancellationToken)
    {
        lock (SyncRoot)
        {
            var bid = PlacementBids.FirstOrDefault(item => item.Id == bidId);
            if (bid is null || bid.Status != PlacementBidStatus.AwaitingPayment)
            {
                return Task.FromResult<PlacementBid?>(null);
            }

            bid.Attempts++;
            bid.UpdatedAt = now;
            return Task.FromResult<PlacementBid?>(bid);
        }
    }

    Task<bool> IPlacementStore.AttachProviderOrderAsync(Guid bidId, string providerOrderId, CancellationToken cancellationToken)
    {
        lock (SyncRoot)
        {
            var bid = PlacementBids.First(item => item.Id == bidId);
            if (bid.ProviderOrderId is not null)
            {
                return Task.FromResult(false);
            }

            bid.ProviderOrderId = providerOrderId;
            return Task.FromResult(true);
        }
    }

    Task<PlacementCaptureOutcome> IPlacementStore.ApplyCapturedAsync(string providerOrderId, string providerPaymentId, long paidPaise, DateTime now, CancellationToken cancellationToken)
    {
        lock (SyncRoot)
        {
            var bid = PlacementBids.FirstOrDefault(item => item.ProviderOrderId == providerOrderId);
            if (bid is null)
            {
                return Task.FromResult(PlacementCaptureOutcome.UnknownPayment);
            }

            var current = PlacementBids.FirstOrDefault(item => item.Pincode == bid.Pincode && item.WeekStart == bid.WeekStart && item.Status == PlacementBidStatus.Leading && item.Id != bid.Id);
            return Task.FromResult(PlacementTransitions.ApplyCaptured(bid, current, providerPaymentId, paidPaise, now));
        }
    }

    Task<IReadOnlyList<PlacementBid>> IPlacementStore.GetRefundsToStartAsync(int max, CancellationToken cancellationToken)
    {
        lock (SyncRoot)
        {
            return Task.FromResult<IReadOnlyList<PlacementBid>>(PlacementBids.Where(bid => bid.PaymentStatus == PaymentState.Refunding && bid.RefundId is null).OrderBy(bid => bid.UpdatedAt).Take(max).ToArray());
        }
    }

    Task IPlacementStore.SetRefundStartedAsync(Guid bidId, string refundId, bool processed, DateTime now, CancellationToken cancellationToken)
    {
        lock (SyncRoot)
        {
            var bid = PlacementBids.First(item => item.Id == bidId);
            bid.RefundId = refundId;
            bid.UpdatedAt = now;
            if (processed)
            {
                bid.PaymentStatus = PaymentState.Refunded;
            }
        }

        return Task.CompletedTask;
    }

    Task IPlacementStore.SetRefundFailedAsync(Guid bidId, string reason, bool permanent, DateTime now, CancellationToken cancellationToken)
    {
        lock (SyncRoot)
        {
            var bid = PlacementBids.First(item => item.Id == bidId);
            bid.FailureReason = reason;
            bid.UpdatedAt = now;
            if (permanent)
            {
                bid.PaymentStatus = PaymentState.RefundFailed;
            }
        }

        return Task.CompletedTask;
    }

    Task IPlacementStore.SetRefundedAsync(Guid bidId, DateTime now, CancellationToken cancellationToken)
    {
        lock (SyncRoot)
        {
            var bid = PlacementBids.First(item => item.Id == bidId);
            bid.PaymentStatus = PaymentState.Refunded;
            bid.UpdatedAt = now;
        }

        return Task.CompletedTask;
    }

    Task<IReadOnlyList<PlacementBid>> IPlacementStore.GetUnsettledAsync(DateTime olderThan, int max, CancellationToken cancellationToken)
    {
        lock (SyncRoot)
        {
            return Task.FromResult<IReadOnlyList<PlacementBid>>(PlacementBids
                .Where(bid => bid.UpdatedAt < olderThan && ((bid.Status == PlacementBidStatus.AwaitingPayment && bid.ProviderOrderId is not null) || (bid.PaymentStatus == PaymentState.Refunding && bid.RefundId is not null)))
                .OrderBy(bid => bid.UpdatedAt).Take(max).ToArray());
        }
    }
    public List<OfferFee> OfferFees { get; } = [];

    public Task<int> GetLoadFeeAsync(CancellationToken cancellationToken = default)
    {
        lock (SyncRoot)
        {
            return Task.FromResult(OfferFees.FirstOrDefault(fee => fee.StoreId is null)?.LoadAmountPaise ?? 0);
        }
    }

    public Task SetLoadFeeAsync(int amountPaise, string? by, DateTime now, CancellationToken cancellationToken = default)
    {
        lock (SyncRoot)
        {
            var row = OfferFees.FirstOrDefault(fee => fee.StoreId is null);
            if (row is null)
            {
                OfferFees.Add(new OfferFee { StoreId = null, AmountPaise = 0, LoadAmountPaise = amountPaise, UpdatedAt = now, UpdatedBy = by });
            }
            else
            {
                row.LoadAmountPaise = amountPaise;
                row.UpdatedAt = now;
                row.UpdatedBy = by;
            }
        }

        return Task.CompletedTask;
    }

    // ---- offer fees ----

    public Task<int> GetDefaultAsync(CancellationToken cancellationToken = default)
    {
        lock (SyncRoot)
        {
            return Task.FromResult(OfferFees.FirstOrDefault(fee => fee.StoreId is null)?.AmountPaise ?? 0);
        }
    }

    public Task<IReadOnlyDictionary<Guid, int>> GetOverridesAsync(CancellationToken cancellationToken = default)
    {
        lock (SyncRoot)
        {
            return Task.FromResult<IReadOnlyDictionary<Guid, int>>(OfferFees.Where(fee => fee.StoreId is not null).ToDictionary(fee => fee.StoreId!.Value, fee => fee.AmountPaise));
        }
    }

    public Task SetAsync(Guid? storeId, int? amountPaise, string? by, DateTime now, CancellationToken cancellationToken = default)
    {
        lock (SyncRoot)
        {
            var row = OfferFees.FirstOrDefault(fee => fee.StoreId == storeId);
            if (amountPaise is null)
            {
                if (row is not null)
                {
                    OfferFees.Remove(row);
                }
            }
            else if (row is null)
            {
                OfferFees.Add(new OfferFee { StoreId = storeId, AmountPaise = amountPaise.Value, UpdatedAt = now, UpdatedBy = by });
            }
            else
            {
                row.AmountPaise = amountPaise.Value;
                row.UpdatedAt = now;
                row.UpdatedBy = by;
            }
        }

        return Task.CompletedTask;
    }

    public Task<IReadOnlyList<Store>> GetStoresAsync(Guid organizationId, CancellationToken cancellationToken = default)
    {
        lock (SyncRoot)
        {
            return Task.FromResult<IReadOnlyList<Store>>(Stores.Where(store => store.OrganizationId == organizationId && store.IsActive).ToArray());
        }
    }

    // ---- offer fee payments ----

    Task<CampaignPayment?> ICampaignPaymentStore.GetPaymentAsync(Guid campaignId, CancellationToken cancellationToken = default)
    {
        lock (SyncRoot)
        {
            return Task.FromResult(CampaignPayments.FirstOrDefault(payment => payment.CampaignId == campaignId));
        }
    }

    Task<CampaignPayment?> ICampaignPaymentStore.BeginAttemptAsync(Guid campaignId, DateTime now, CancellationToken cancellationToken = default)
    {
        lock (SyncRoot)
        {
            var campaign = Campaigns.FirstOrDefault(item => item.Id == campaignId);
            if (campaign is null || !CampaignMapping.AwaitsPayment(campaign))
            {
                return Task.FromResult<CampaignPayment?>(null);
            }

            var payment = CampaignPayments.FirstOrDefault(item => item.CampaignId == campaignId);
            if (payment is null)
            {
                payment = new CampaignPayment { CampaignId = campaignId, AmountPaise = campaign.FeePaise, CreatedAt = now, UpdatedAt = now };
                CampaignPayments.Add(payment);
            }

            payment.Attempts++;
            payment.UpdatedAt = now;
            if (payment.Status == PaymentState.Failed)
            {
                payment.Status = PaymentState.Created;
                campaign.PaymentStatus = PaymentState.Created;
            }

            return Task.FromResult<CampaignPayment?>(payment);
        }
    }

    Task<bool> ICampaignPaymentStore.AttachProviderOrderAsync(Guid paymentId, string providerOrderId, CancellationToken cancellationToken = default)
    {
        lock (SyncRoot)
        {
            var payment = CampaignPayments.First(item => item.Id == paymentId);
            if (payment.ProviderOrderId is not null)
            {
                return Task.FromResult(false);
            }

            payment.ProviderOrderId = providerOrderId;
            return Task.FromResult(true);
        }
    }

    Task<CapturedOutcome> ICampaignPaymentStore.ApplyCapturedAsync(string providerOrderId, string providerPaymentId, long paidPaise, DateTime now, CancellationToken cancellationToken = default)
    {
        lock (SyncRoot)
        {
            var payment = CampaignPayments.FirstOrDefault(item => item.ProviderOrderId == providerOrderId);
            if (payment is null)
            {
                return Task.FromResult(CapturedOutcome.UnknownPayment);
            }

            return Task.FromResult(CampaignPaymentTransitions.ApplyCaptured(Campaigns.Single(item => item.Id == payment.CampaignId), payment, providerPaymentId, paidPaise, now));
        }
    }

    Task<IReadOnlyList<CampaignPayment>> ICampaignPaymentStore.GetRefundsToStartAsync(int max, CancellationToken cancellationToken)
    {
        lock (SyncRoot)
        {
            return Task.FromResult<IReadOnlyList<CampaignPayment>>(CampaignPayments.Where(payment => payment.Status == PaymentState.Refunding && payment.RefundId is null).OrderBy(payment => payment.UpdatedAt).Take(max).ToArray());
        }
    }

    Task ICampaignPaymentStore.SetRefundStartedAsync(Guid paymentId, string refundId, bool processed, DateTime now, CancellationToken cancellationToken)
    {
        lock (SyncRoot)
        {
            var payment = CampaignPayments.First(item => item.Id == paymentId);
            payment.RefundId = refundId;
            payment.UpdatedAt = now;
            if (processed)
            {
                CampaignPaymentTransitions.ApplyRefunded(Campaigns.Single(item => item.Id == payment.CampaignId), payment, now);
            }
        }

        return Task.CompletedTask;
    }

    Task ICampaignPaymentStore.SetRefundFailedAsync(Guid paymentId, string reason, bool permanent, DateTime now, CancellationToken cancellationToken)
    {
        lock (SyncRoot)
        {
            var payment = CampaignPayments.First(item => item.Id == paymentId);
            if (permanent)
            {
                CampaignPaymentTransitions.ApplyRefundFailed(Campaigns.Single(item => item.Id == payment.CampaignId), payment, reason, now);
            }
            else
            {
                payment.FailureReason = reason;
                payment.UpdatedAt = now;
            }
        }

        return Task.CompletedTask;
    }

    public Task SetRefundedAsync(Guid paymentId, DateTime now, CancellationToken cancellationToken = default)
    {
        lock (SyncRoot)
        {
            var payment = CampaignPayments.First(item => item.Id == paymentId);
            CampaignPaymentTransitions.ApplyRefunded(Campaigns.Single(item => item.Id == payment.CampaignId), payment, now);
        }

        return Task.CompletedTask;
    }

    Task<IReadOnlyList<CampaignPayment>> ICampaignPaymentStore.GetUnsettledAsync(DateTime olderThan, int max, CancellationToken cancellationToken)
    {
        lock (SyncRoot)
        {
            return Task.FromResult<IReadOnlyList<CampaignPayment>>(CampaignPayments
                .Where(payment => payment.UpdatedAt < olderThan &&
                    (((payment.Status is PaymentState.Created or PaymentState.Failed) && payment.ProviderOrderId is not null) || (payment.Status == PaymentState.Refunding && payment.RefundId is not null)))
                .OrderBy(payment => payment.UpdatedAt).Take(max).ToArray());
        }
    }

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
            foreach (var link in campaign.Stores)
            {
                link.CampaignId = campaign.Id;
                link.Store = Stores.First(item => item.Id == link.StoreId);
            }

            foreach (var link in campaign.Products)
            {
                link.CampaignId = campaign.Id;
            }

            Campaigns.Add(campaign);
        }

        return Task.CompletedTask;
    }

    public Task<IReadOnlyList<Store>> GetStoresAsync(IReadOnlyCollection<Guid> ids, Guid organizationId, CancellationToken cancellationToken = default)
    {
        lock (SyncRoot)
        {
            return Task.FromResult<IReadOnlyList<Store>>(Stores.Where(item => ids.Contains(item.Id) && item.OrganizationId == organizationId && item.IsActive).ToArray());
        }
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
            if (campaign is not null && campaign.PublishedAt is null && campaign.IsActive)
            {
                campaign.IsActive = false;
                CampaignPaymentTransitions.ApplyCancelled(campaign, CampaignPayments.FirstOrDefault(item => item.CampaignId == id), DateTime.UtcNow);
            }

            return Task.FromResult(campaign);
        }
    }

    public Task<IReadOnlyList<Product>> GetActiveProductsAsync(IReadOnlyCollection<Guid> ids, CancellationToken cancellationToken = default)
    {
        lock (SyncRoot)
        {
            return Task.FromResult<IReadOnlyList<Product>>(Products.Where(item => ids.Contains(item.Id) && item.IsActive).ToArray());
        }
    }

    public Task<IReadOnlyList<Product>> SearchActiveProductsAsync(string? search, int take, CancellationToken cancellationToken = default)
    {
        lock (SyncRoot)
        {
            return Task.FromResult<IReadOnlyList<Product>>(Products.Where(item => item.IsActive && (search is null || item.Name.Contains(search, StringComparison.OrdinalIgnoreCase))).OrderBy(item => item.Name).Take(take).ToArray());
        }
    }

    public Task<IReadOnlyDictionary<Guid, string>> GetProductNamesAsync(IReadOnlyCollection<Guid> ids, CancellationToken cancellationToken = default)
    {
        lock (SyncRoot)
        {
            return Task.FromResult<IReadOnlyDictionary<Guid, string>>(Products.Where(item => ids.Contains(item.Id)).ToDictionary(item => item.Id, item => item.Name));
        }
    }

    public Task<Campaign?> TryEndDiscountAsync(Guid id, DateTime now, CancellationToken cancellationToken = default)
    {
        lock (SyncRoot)
        {
            var campaign = Campaigns.FirstOrDefault(item => item.Id == id);
            if (campaign is not null && OfferRules.DiscountRunning(campaign, now))
            {
                campaign.EndsAt = now;
            }

            return Task.FromResult(campaign);
        }
    }

    public Task<IReadOnlyDictionary<Guid, int>> GetPercentsAsync(Guid storeId, IReadOnlyCollection<Guid> productIds, DateTime now, CancellationToken cancellationToken = default)
    {
        lock (SyncRoot)
        {
            var percents = Campaigns
                .Where(campaign => OfferRules.DiscountRunning(campaign, now) && OfferRules.AppliesToStore(campaign, storeId))
                .SelectMany(campaign => campaign.Products.Where(link => productIds.Contains(link.ProductId)).Select(link => (link.ProductId, campaign.DiscountPercent)))
                .GroupBy(entry => entry.ProductId)
                .ToDictionary(group => group.Key, group => OfferPricing.Best(group.Select(entry => entry.DiscountPercent)));
            return Task.FromResult<IReadOnlyDictionary<Guid, int>>(percents);
        }
    }

    public Task<OfferDetail?> GetOfferAsync(Guid id, DateTime now, CancellationToken cancellationToken = default)
    {
        lock (SyncRoot)
        {
            var campaign = Campaigns.FirstOrDefault(item => item.Id == id);
            return Task.FromResult(campaign is not null && OfferRules.IsOpenable(campaign, now) ? OfferRules.Detail(campaign, now) : null);
        }
    }

    public Task<IReadOnlyList<ActiveOffer>> GetActiveOffersAsync(Guid storeId, DateTime now, CancellationToken cancellationToken = default)
    {
        lock (SyncRoot)
        {
            return Task.FromResult<IReadOnlyList<ActiveOffer>>(Campaigns
                .Where(campaign => OfferRules.ShowsAsBanner(campaign, now) && OfferRules.AppliesToStore(campaign, storeId))
                .OrderByDescending(campaign => campaign.StartsAt)
                .Select(campaign => new ActiveOffer(campaign.Id, campaign.TitleEn, string.IsNullOrEmpty(campaign.BodyEn) ? null : campaign.BodyEn, campaign.TitleMr, campaign.BodyMr, campaign.ImageUrl, campaign.DiscountPercent, campaign.EndsAt, campaign.Products.Select(link => link.ProductId).ToArray(), campaign.ShowOnLoad))
                .ToArray());
        }
    }

    public Task<int> PublishDueCampaignsAsync(DateTime now, CancellationToken cancellationToken = default)
    {
        lock (SyncRoot)
        {
            var sent = 0;
            foreach (var campaign in Campaigns.Where(item => item.IsActive && item.PublishedAt is null && item.StartsAt <= now && item.PaymentStatus is PaymentState.NotRequired or PaymentState.Paid).ToArray())
            {
                campaign.PublishedAt = now;
                var storeIds = campaign.Stores.Select(link => link.StoreId).ToHashSet();
                // A customer of a store: has ordered from it or has a cart there.
                string? KnownStore(Customer customer) => campaign.Stores
                    .Where(link => Orders.Any(order => order.UserId == customer.UserId && order.StoreId == link.StoreId) || Carts.Any(cart => cart.CustomerId == customer.Id && cart.StoreId == link.StoreId))
                    .Select(link => link.Store.Name)
                    .Order()
                    .FirstOrDefault();
                var recipients = campaign.Notify
                    ? Customers.Where(customer => customer.IsActive && customer.OffersEnabled && (storeIds.Count == 0 || KnownStore(customer) is not null)).ToArray()
                    : [];
                foreach (var customer in recipients)
                {
                    var store = KnownStore(customer);
                    Notifications.Add(new Notification
                    {
                        CustomerId = customer.Id,
                        Type = NotificationTypes.Offer,
                        Category = NotificationCategories.Offer,
                        Title = campaign.TitleEn,
                        Message = campaign.BodyEn,
                        DataJson = store is null ? null : System.Text.Json.JsonSerializer.Serialize(new Dictionary<string, string> { ["store"] = store }),
                        CampaignId = campaign.Id,
                        Campaign = campaign
                    });
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
            var percents = GetPercentsAsync(storeId, cart.Items.Select(item => item.ProductId).Distinct().ToArray(), commit.Now ?? DateTime.UtcNow).GetAwaiter().GetResult();
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

                var offerPercent = percents.GetValueOrDefault(product.Id);
                var unitPrice = OfferPricing.Apply(variant.Price, offerPercent);
                if (unitPrice != cartItem.UnitPriceSnapshot)
                {
                    repriced.Add(new CheckoutIssue(cartItem.ProductId, cartItem.VariantId, product.Name, variant.Label, cartItem.Quantity, OldPrice: cartItem.UnitPriceSnapshot, NewPrice: unitPrice));
                    continue;
                }

                orderItems.Add(new OrderItem
                {
                    ProductId = product.Id,
                    VariantId = variant.Id,
                    ProductNameSnapshot = product.Name,
                    VariantLabelSnapshot = variant.Label,
                    UnitPrice = unitPrice,
                    // With an offer the receipt shows what it was worth before: the MRP, or the normal price when there is none.
                    UnitMrpSnapshot = offerPercent > 0 ? CatalogPricing.EffectiveMrp(variant.Price, variant.Mrp) : variant.Mrp,
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

            if (!OrderStatusTransitions.IsValid(order.Status, targetStatus) || (order.DeliveryPartnerId.HasValue && targetStatus == OrderStatus.Completed))
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

    Task<User?> IDeliveryLookup.GetPartnerAsync(Guid userId, CancellationToken cancellationToken)
    {
        lock (SyncRoot)
        {
            return Task.FromResult(Users.FirstOrDefault(user => user.Id == userId && user.Role == Role.DeliveryPartner));
        }
    }

    Task<IReadOnlyList<User>> IDeliveryStore.ListPartnersAsync(Guid organizationId, Guid storeId, CancellationToken cancellationToken)
    {
        lock (SyncRoot)
        {
            return Task.FromResult<IReadOnlyList<User>>(Users.Where(user => user.Role == Role.DeliveryPartner && user.IsActive && user.OrganizationId == organizationId && user.StoreId == storeId).ToArray());
        }
    }

    Task<IReadOnlyDictionary<Guid, int>> IDeliveryStore.CountActiveAsync(IReadOnlyCollection<Guid> partnerIds, CancellationToken cancellationToken)
    {
        lock (SyncRoot)
        {
            return Task.FromResult<IReadOnlyDictionary<Guid, int>>(Orders
                .Where(order => order.DeliveryPartnerId.HasValue && partnerIds.Contains(order.DeliveryPartnerId.Value) && (order.Status == OrderStatus.Ready || order.Status == OrderStatus.OutForDelivery))
                .GroupBy(order => order.DeliveryPartnerId!.Value)
                .ToDictionary(group => group.Key, group => group.Count()));
        }
    }

    Task<DeliveryChange> IDeliveryStore.AssignAsync(Guid orderId, Guid organizationId, Guid? scopeStoreId, Guid partnerId, string otp, DateTime now, CancellationToken cancellationToken)
    {
        lock (SyncRoot)
        {
            var order = Orders.FirstOrDefault(item => item.Id == orderId && (!scopeStoreId.HasValue || item.StoreId == scopeStoreId.Value) &&
                Stores.Any(store => store.Id == item.StoreId && store.OrganizationId == organizationId));
            if (order is null)
            {
                return Task.FromResult(new DeliveryChange(DeliveryChangeStatus.NotFound));
            }

            var partnerOk = Users.Any(user => user.Id == partnerId && user.Role == Role.DeliveryPartner && user.IsActive && user.OrganizationId == organizationId && user.StoreId == order.StoreId);
            return Task.FromResult(partnerOk ? new DeliveryChange(DeliveryRules.Assign(order, partnerId, otp, now), order) : new DeliveryChange(DeliveryChangeStatus.PartnerInvalid));
        }
    }

    Task<IReadOnlyList<(Order Order, Store Store)>> IDeliveryStore.ListForPartnerAsync(Guid partnerId, DateTime deliveredSince, CancellationToken cancellationToken)
    {
        lock (SyncRoot)
        {
            return Task.FromResult<IReadOnlyList<(Order Order, Store Store)>>(Orders
                .Where(order => order.DeliveryPartnerId == partnerId &&
                    (order.Status == OrderStatus.Ready || order.Status == OrderStatus.OutForDelivery || (order.Status == OrderStatus.Delivered && order.DeliveredAt >= deliveredSince)))
                .OrderByDescending(order => order.AssignedAt)
                .Select(order => (order, Stores.First(store => store.Id == order.StoreId)))
                .ToArray());
        }
    }

    Task<DeliveryChange> IDeliveryStore.PickUpAsync(Guid orderId, Guid partnerId, DateTime now, CancellationToken cancellationToken)
    {
        lock (SyncRoot)
        {
            var order = Orders.FirstOrDefault(item => item.Id == orderId && item.DeliveryPartnerId == partnerId);
            if (order is null)
            {
                return Task.FromResult(new DeliveryChange(DeliveryChangeStatus.NotFound));
            }

            var status = DeliveryRules.PickUp(order, partnerId, now);
            if (status == DeliveryChangeStatus.Succeeded)
            {
                NotifyDelivery(order, OrderStatus.OutForDelivery);
            }

            return Task.FromResult(new DeliveryChange(status, order));
        }
    }

    Task<DeliveryChange> IDeliveryStore.DeliverAsync(Guid orderId, Guid partnerId, string otp, decimal cashCollected, DateTime now, CancellationToken cancellationToken)
    {
        lock (SyncRoot)
        {
            var order = Orders.FirstOrDefault(item => item.Id == orderId && item.DeliveryPartnerId == partnerId);
            if (order is null)
            {
                return Task.FromResult(new DeliveryChange(DeliveryChangeStatus.NotFound));
            }

            var status = DeliveryRules.Deliver(order, partnerId, otp, cashCollected, now);
            if (status == DeliveryChangeStatus.Succeeded)
            {
                NotifyDelivery(order, OrderStatus.Delivered);
            }

            return Task.FromResult(new DeliveryChange(status, order));
        }
    }

    private void NotifyDelivery(Order order, OrderStatus status)
    {
        var customer = Customers.FirstOrDefault(item => item.UserId == order.UserId);
        if (customer is not null)
        {
            Notifications.Add(NotificationCatalog.ForStatus(customer.Id, order, status));
        }
    }

    private ProductBundle BundleOf(Product product) => new(
        product,
        Categories.FirstOrDefault(category => category.Id == product.CategoryId)?.Name ?? string.Empty,
        Variants.Where(variant => variant.ProductId == product.Id).ToArray(),
        ProductTranslations.Where(translation => translation.ProductId == product.Id).ToArray());

    private bool SkuInUse(string sku, Guid? exceptVariantId, Guid? exceptProductId) =>
        Variants.Any(variant => variant.Sku == sku && variant.Id != exceptVariantId) || Products.Any(product => product.Sku == sku && product.Id != exceptProductId);

    Task<(IReadOnlyList<ProductBundle> Items, int Total)> ICatalogAdminStore.ListProductsAsync(string? search, int skip, int take, CancellationToken cancellationToken)
    {
        lock (SyncRoot)
        {
            var matches = Products
                .Where(product => search is null || product.Name.Contains(search, StringComparison.OrdinalIgnoreCase) || product.Sku.Contains(search, StringComparison.OrdinalIgnoreCase))
                .OrderBy(product => product.Name).ThenBy(product => product.Id).ToArray();
            return Task.FromResult<(IReadOnlyList<ProductBundle>, int)>((matches.Skip(skip).Take(take).Select(BundleOf).ToArray(), matches.Length));
        }
    }

    Task<ProductBundle?> ICatalogAdminStore.GetProductAsync(Guid productId, CancellationToken cancellationToken)
    {
        lock (SyncRoot)
        {
            var product = Products.FirstOrDefault(item => item.Id == productId);
            return Task.FromResult(product is null ? null : BundleOf(product));
        }
    }

    Task<CatalogChange> ICatalogAdminStore.CreateProductAsync(Product product, ProductVariant firstVariant, CancellationToken cancellationToken)
    {
        lock (SyncRoot)
        {
            if (!Categories.Any(category => category.Id == product.CategoryId && category.IsActive))
            {
                return Task.FromResult(CatalogChange.CategoryMissing);
            }

            if (SkuInUse(product.Sku, null, null))
            {
                return Task.FromResult(CatalogChange.DuplicateSku);
            }

            Products.Add(product);
            Variants.Add(firstVariant);
            return Task.FromResult(CatalogChange.Succeeded);
        }
    }

    Task<CatalogChange> ICatalogAdminStore.UpdateProductAsync(Guid productId, string name, string description, Guid categoryId, string unitOfMeasure, bool isActive, CancellationToken cancellationToken)
    {
        lock (SyncRoot)
        {
            var product = Products.FirstOrDefault(item => item.Id == productId);
            if (product is null)
            {
                return Task.FromResult(CatalogChange.NotFound);
            }

            if (!Categories.Any(category => category.Id == categoryId && (category.IsActive || category.Id == product.CategoryId)))
            {
                return Task.FromResult(CatalogChange.CategoryMissing);
            }

            product.Name = name;
            product.Description = description;
            product.CategoryId = categoryId;
            product.UnitOfMeasure = unitOfMeasure;
            product.IsActive = isActive;
            return Task.FromResult(CatalogChange.Succeeded);
        }
    }

    Task<CatalogChange> ICatalogAdminStore.SaveTranslationAsync(Guid productId, string locale, string name, string? description, CancellationToken cancellationToken)
    {
        lock (SyncRoot)
        {
            if (!Products.Any(item => item.Id == productId))
            {
                return Task.FromResult(CatalogChange.NotFound);
            }

            ProductTranslations.RemoveAll(item => item.ProductId == productId && item.Locale == locale);
            ProductTranslations.Add(new ProductTranslation { ProductId = productId, Locale = locale, Name = name, Description = description });
            return Task.FromResult(CatalogChange.Succeeded);
        }
    }

    Task<CatalogChange> ICatalogAdminStore.DeleteTranslationAsync(Guid productId, string locale, CancellationToken cancellationToken)
    {
        lock (SyncRoot)
        {
            if (!Products.Any(item => item.Id == productId))
            {
                return Task.FromResult(CatalogChange.NotFound);
            }

            ProductTranslations.RemoveAll(item => item.ProductId == productId && item.Locale == locale);
            return Task.FromResult(CatalogChange.Succeeded);
        }
    }

    Task<CatalogChange> ICatalogAdminStore.AddVariantAsync(Guid productId, ProductVariant variant, CancellationToken cancellationToken)
    {
        lock (SyncRoot)
        {
            if (!Products.Any(item => item.Id == productId))
            {
                return Task.FromResult(CatalogChange.NotFound);
            }

            if (SkuInUse(variant.Sku, null, null))
            {
                return Task.FromResult(CatalogChange.DuplicateSku);
            }

            variant.IsDefault = false;
            Variants.Add(variant);
            return Task.FromResult(CatalogChange.Succeeded);
        }
    }

    Task<CatalogChange> ICatalogAdminStore.UpdateVariantAsync(Guid productId, Guid variantId, string sku, string label, decimal price, decimal? mrp, int sortOrder, bool isActive, CancellationToken cancellationToken)
    {
        lock (SyncRoot)
        {
            var product = Products.FirstOrDefault(item => item.Id == productId);
            var variants = Variants.Where(item => item.ProductId == productId).ToArray();
            var variant = variants.FirstOrDefault(item => item.Id == variantId);
            if (product is null || variant is null)
            {
                return Task.FromResult(CatalogChange.NotFound);
            }

            if (!string.Equals(variant.Sku, sku, StringComparison.Ordinal) && SkuInUse(sku, variantId, productId))
            {
                return Task.FromResult(CatalogChange.DuplicateSku);
            }

            return Task.FromResult(CatalogRules.UpdateVariant(product, variants, variant, sku, label, price, mrp, sortOrder, isActive));
        }
    }

    Task<CatalogChange> ICatalogAdminStore.MakeDefaultAsync(Guid productId, Guid variantId, CancellationToken cancellationToken)
    {
        lock (SyncRoot)
        {
            var product = Products.FirstOrDefault(item => item.Id == productId);
            var variants = Variants.Where(item => item.ProductId == productId).ToArray();
            var target = variants.FirstOrDefault(item => item.Id == variantId);
            if (product is null || target is null)
            {
                return Task.FromResult(CatalogChange.NotFound);
            }

            if (!target.IsActive)
            {
                return Task.FromResult(CatalogChange.VariantInactive);
            }

            foreach (var current in variants)
            {
                current.IsDefault = current.Id == variantId;
            }

            CatalogRules.SyncFromDefault(product, variants);
            return Task.FromResult(CatalogChange.Succeeded);
        }
    }

    Task<(CatalogChange Change, string? Previous)> ICatalogAdminStore.SetImageAsync(Guid productId, string? publicPath, CancellationToken cancellationToken)
    {
        lock (SyncRoot)
        {
            var product = Products.FirstOrDefault(item => item.Id == productId);
            if (product is null)
            {
                return Task.FromResult<(CatalogChange, string?)>((CatalogChange.NotFound, null));
            }

            var previous = product.ImageUrl;
            product.ImageUrl = publicPath;
            return Task.FromResult<(CatalogChange, string?)>((CatalogChange.Succeeded, previous));
        }
    }

    Task<Store?> ICatalogAdminStore.GetStoreForStockAsync(Guid storeId, Guid organizationId, CancellationToken cancellationToken)
    {
        lock (SyncRoot)
        {
            return Task.FromResult(Stores.FirstOrDefault(store => store.Id == storeId && store.OrganizationId == organizationId && store.IsActive));
        }
    }

    Task<IReadOnlyList<StockRowResponse>> ICatalogAdminStore.ListStockAsync(Guid storeId, CancellationToken cancellationToken)
    {
        lock (SyncRoot)
        {
            return Task.FromResult<IReadOnlyList<StockRowResponse>>(Variants
                .Where(variant => variant.IsActive)
                .Join(Products.Where(product => product.IsActive), variant => variant.ProductId, product => product.Id, (variant, product) => (variant, product))
                .OrderBy(row => row.product.Name).ThenBy(row => row.variant.SortOrder)
                .Select(row =>
                {
                    var stock = Inventory.FirstOrDefault(item => item.StoreId == storeId && item.VariantId == row.variant.Id);
                    return new StockRowResponse(row.product.Id, row.product.Name, row.variant.Id, row.variant.Label, stock is not null, stock?.AvailableQuantity ?? 0, stock?.ReorderThreshold ?? 5);
                }).ToArray());
        }
    }

    Task<StockRowResponse?> ICatalogAdminStore.SetStockAsync(Guid storeId, Guid variantId, bool carried, int available, int reorderThreshold, CancellationToken cancellationToken)
    {
        lock (SyncRoot)
        {
            var variant = Variants.FirstOrDefault(item => item.Id == variantId);
            var product = variant is null ? null : Products.FirstOrDefault(item => item.Id == variant.ProductId);
            if (variant is null || product is null)
            {
                return Task.FromResult<StockRowResponse?>(null);
            }

            var row = Inventory.FirstOrDefault(item => item.StoreId == storeId && item.VariantId == variantId);
            if (!carried)
            {
                if (row is not null)
                {
                    Inventory.Remove(row);
                }
            }
            else if (row is null)
            {
                Inventory.Add(new StoreVariantInventory { StoreId = storeId, VariantId = variantId, AvailableQuantity = available, ReorderThreshold = reorderThreshold });
            }
            else
            {
                row.AvailableQuantity = available;
                row.ReorderThreshold = reorderThreshold;
            }

            return Task.FromResult<StockRowResponse?>(new StockRowResponse(product.Id, product.Name, variant.Id, variant.Label, carried, carried ? available : 0, carried ? reorderThreshold : 5));
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
