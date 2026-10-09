using Microsoft.EntityFrameworkCore;
using QuickCommerce.Application.DTOs;
using QuickCommerce.Application.Interfaces;
using QuickCommerce.Domain;

namespace QuickCommerce.Infrastructure.Persistence;

public sealed class EfCommerceStore(QuickCommerceDbContext db) : ICommerceStore
{
    public async Task<AdminDashboardSnapshot> GetAdminDashboardAsync(Guid organizationId, IReadOnlySet<Guid> storeIds, bool isApplicationAdmin, CancellationToken cancellationToken = default)
    {
        var organizationStores = db.Stores.AsNoTracking().Where(store => store.OrganizationId == organizationId);
        var scopedStoreQuery = organizationStores.Where(store => store.IsActive);
        if (!isApplicationAdmin)
        {
            scopedStoreQuery = scopedStoreQuery.Where(store => storeIds.Contains(store.Id));
        }

        var scopedStoreIds = await scopedStoreQuery.Select(store => store.Id).ToArrayAsync(cancellationToken);
        var totalStores = isApplicationAdmin
            ? await organizationStores.CountAsync(cancellationToken)
            : await organizationStores.CountAsync(store => storeIds.Contains(store.Id), cancellationToken);
        var orders = db.Orders.AsNoTracking().Where(order => scopedStoreIds.Contains(order.StoreId));
        var today = DateTime.UtcNow.Date;
        var activeStatuses = new[] { OrderStatus.Pending, OrderStatus.Accepted, OrderStatus.Preparing, OrderStatus.Ready, OrderStatus.Confirmed, OrderStatus.OutForDelivery };

        var activeOrderRows = await orders.Where(order => activeStatuses.Contains(order.Status))
            .Join(db.Users.AsNoTracking(), order => order.UserId, user => user.Id, (order, user) => new { order, customer = user.DisplayName })
            .Join(db.Stores.AsNoTracking(), row => row.order.StoreId, store => store.Id, (row, store) => new
            {
                row.order.Id,
                row.order.OrderNumber,
                Customer = row.customer,
                Store = store.Name,
                row.order.Status,
                OrderTime = row.order.CreatedAt,
                Amount = row.order.TotalAmount
            })
            .OrderByDescending(row => row.OrderTime)
            .ToArrayAsync(cancellationToken);

        var activeOrders = activeOrderRows
            .Select(row => new ActiveOrderSummary(row.Id, row.OrderNumber, row.Customer, row.Store, row.Status, row.OrderTime, row.Amount, null))
            .ToArray();
        return new AdminDashboardSnapshot(
            totalStores,
            scopedStoreIds.Length,
            await db.Products.AsNoTracking().CountAsync(cancellationToken),
            await db.Products.AsNoTracking().CountAsync(product => product.IsActive, cancellationToken),
            await db.Customers.AsNoTracking().CountAsync(customer => customer.IsActive && db.Users.Any(user => user.Id == customer.UserId && user.OrganizationId == organizationId), cancellationToken),
            await orders.CountAsync(cancellationToken),
            await orders.CountAsync(order => order.CreatedAt >= today, cancellationToken),
            activeOrders.Length,
            await orders.CountAsync(order => order.Status == OrderStatus.Completed, cancellationToken),
            await orders.CountAsync(order => order.Status == OrderStatus.Pending, cancellationToken),
            await orders.CountAsync(order => order.Status == OrderStatus.Cancelled, cancellationToken),
            await orders.Where(order => order.Status == OrderStatus.Completed).SumAsync(order => (decimal?)order.TotalAmount, cancellationToken) ?? 0,
            await orders.Where(order => order.Status == OrderStatus.Completed && order.CreatedAt >= today).SumAsync(order => (decimal?)order.TotalAmount, cancellationToken) ?? 0,
            activeOrders);
    }
    public async Task<IReadOnlyList<Category>> GetCategoriesAsync(CancellationToken cancellationToken = default) => await db.Categories.AsNoTracking().ToListAsync(cancellationToken);

    public async Task<IReadOnlyList<Product>> GetProductsAsync(string? search, Guid? categoryId, CancellationToken cancellationToken = default)
    {
        var query = db.Products.AsNoTracking();
        if (!string.IsNullOrWhiteSpace(search))
        {
            query = query.Where(product => product.Name.Contains(search) || product.Description.Contains(search));
        }

        if (categoryId.HasValue)
        {
            query = query.Where(product => product.CategoryId == categoryId.Value);
        }

        return await query.ToListAsync(cancellationToken);
    }

    public Task<(IReadOnlyList<Product> Items, int TotalCount)> GetActiveProductPageAsync(string? search, Guid? categoryId, int skip, int take, CancellationToken cancellationToken = default) =>
        PageAsync(db.Products.AsNoTracking().Where(product => product.IsActive), search, categoryId, skip, take, cancellationToken);

    public Task<(IReadOnlyList<Product> Items, int TotalCount)> GetStoreProductPageAsync(Guid storeId, string? search, Guid? categoryId, int skip, int take, CancellationToken cancellationToken = default) =>
        PageAsync(db.Products.AsNoTracking().Where(product => product.IsActive && db.ProductVariants.Any(variant => variant.ProductId == product.Id && variant.IsActive && db.StoreVariantInventory.Any(row => row.StoreId == storeId && row.VariantId == variant.Id))), search, categoryId, skip, take, cancellationToken);

    public async Task<IReadOnlyList<Guid>> GetCarriedCategoryIdsAsync(Guid storeId, CancellationToken cancellationToken = default) =>
        await db.Products.AsNoTracking()
            .Where(product => product.IsActive && db.ProductVariants.Any(variant => variant.ProductId == product.Id && variant.IsActive && db.StoreVariantInventory.Any(row => row.StoreId == storeId && row.VariantId == variant.Id)))
            .Select(product => product.CategoryId).Distinct().ToListAsync(cancellationToken);

    public async Task<IReadOnlyList<Guid>> GetStoreIdsCarryingProductsAsync(CancellationToken cancellationToken = default) =>
        await db.StoreVariantInventory.AsNoTracking()
            .Where(row => db.ProductVariants.Any(variant => variant.Id == row.VariantId && variant.IsActive && db.Products.Any(product => product.Id == variant.ProductId && product.IsActive)))
            .Select(row => row.StoreId).Distinct().ToListAsync(cancellationToken);

    private async Task<(IReadOnlyList<Product> Items, int TotalCount)> PageAsync(IQueryable<Product> query, string? search, Guid? categoryId, int skip, int take, CancellationToken cancellationToken)
    {
        if (!string.IsNullOrWhiteSpace(search))
        {
            query = query.Where(product => product.Name.Contains(search) || product.Description.Contains(search)
                || db.ProductTranslations.Any(translation => translation.ProductId == product.Id && (translation.Name.Contains(search) || (translation.Description != null && translation.Description.Contains(search)))));
        }

        if (categoryId.HasValue)
        {
            query = query.Where(product => product.CategoryId == categoryId.Value);
        }

        var total = await query.CountAsync(cancellationToken);
        var items = await query.OrderBy(product => product.Name).ThenBy(product => product.Id).Skip(skip).Take(take).ToListAsync(cancellationToken);
        return (items, total);
    }

    public async Task<IReadOnlyList<StoreVariantInventory>> GetStoreVariantInventoryAsync(Guid storeId, IReadOnlyCollection<Guid> variantIds, CancellationToken cancellationToken = default) =>
        variantIds.Count == 0 ? [] : await db.StoreVariantInventory.AsNoTracking().Where(row => row.StoreId == storeId && variantIds.Contains(row.VariantId)).ToListAsync(cancellationToken);

    public async Task<IReadOnlyList<ProductVariant>> GetVariantsAsync(IReadOnlyCollection<Guid> productIds, CancellationToken cancellationToken = default) =>
        productIds.Count == 0 ? [] : await db.ProductVariants.AsNoTracking().Where(variant => productIds.Contains(variant.ProductId)).OrderBy(variant => variant.SortOrder).ThenBy(variant => variant.Price).ToListAsync(cancellationToken);

    public Task<ProductVariant?> GetVariantAsync(Guid variantId, CancellationToken cancellationToken = default) =>
        db.ProductVariants.AsNoTracking().FirstOrDefaultAsync(variant => variant.Id == variantId, cancellationToken);

    public async Task<IReadOnlyList<ProductTranslation>> GetProductTranslationsAsync(IReadOnlyCollection<Guid> productIds, CancellationToken cancellationToken = default) =>
        productIds.Count == 0 ? [] : await db.ProductTranslations.AsNoTracking().Where(translation => productIds.Contains(translation.ProductId)).ToListAsync(cancellationToken);

    public async Task<IReadOnlyList<CategoryTranslation>> GetCategoryTranslationsAsync(CancellationToken cancellationToken = default) =>
        await db.CategoryTranslations.AsNoTracking().ToListAsync(cancellationToken);

    public Task<Product?> GetProductAsync(Guid id, CancellationToken cancellationToken = default) => db.Products.AsNoTracking().FirstOrDefaultAsync(product => product.Id == id, cancellationToken);

    public async Task<IReadOnlyList<Store>> GetStoresAsync(CancellationToken cancellationToken = default) => await db.Stores.AsNoTracking().ToListAsync(cancellationToken);

    public async Task<IReadOnlyList<Store>> GetScopedStoresAsync(Guid organizationId, IReadOnlySet<Guid> storeIds, bool isApplicationAdmin, CancellationToken cancellationToken = default)
    {
        var query = db.Stores.AsNoTracking().Where(store => store.OrganizationId == organizationId && store.IsActive);
        if (!isApplicationAdmin)
        {
            query = query.Where(store => storeIds.Contains(store.Id));
        }

        return await query.OrderBy(store => store.Name).ToListAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<StoreVariantInventory>> GetVariantInventoryAsync(CancellationToken cancellationToken = default) => await db.StoreVariantInventory.AsNoTracking().ToListAsync(cancellationToken);

    public async Task<IReadOnlyList<Order>> GetOrdersAsync(CancellationToken cancellationToken = default) => await db.Orders
        .AsNoTracking()
        .Include(order => order.Items)
        .Include(order => order.StatusHistory)
        .ToListAsync(cancellationToken);

    public async Task<IReadOnlyList<Order>> GetScopedOrdersAsync(Guid organizationId, IReadOnlySet<Guid> storeIds, bool isApplicationAdmin, CancellationToken cancellationToken = default)
    {
        var query = db.Orders.AsNoTracking()
            .Where(order => db.Stores.Any(store => store.Id == order.StoreId && store.OrganizationId == organizationId && store.IsActive));
        if (!isApplicationAdmin)
        {
            query = query.Where(order => storeIds.Contains(order.StoreId));
        }

        return await query.Include(order => order.Items).Include(order => order.StatusHistory).OrderByDescending(order => order.CreatedAt).ToListAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<AdminOrderResponse>> GetScopedOrderSummariesAsync(Guid organizationId, IReadOnlySet<Guid> storeIds, bool isApplicationAdmin, CancellationToken cancellationToken = default)
    {
        var query = db.Orders.AsNoTracking()
            .Join(db.Users.AsNoTracking(), order => order.UserId, user => user.Id, (order, user) => new { order, customer = user.DisplayName })
            .Join(db.Stores.AsNoTracking().Where(store => store.OrganizationId == organizationId && store.IsActive), row => row.order.StoreId, store => store.Id, (row, store) => new { row.order, row.customer, store });
        if (!isApplicationAdmin)
        {
            query = query.Where(row => storeIds.Contains(row.order.StoreId));
        }

        return await query.OrderByDescending(row => row.order.CreatedAt).Select(row => new AdminOrderResponse(row.order.Id, row.order.OrderNumber, row.order.UserId, row.customer, row.order.StoreId, row.store.Name, row.order.TotalAmount, row.order.Status, row.order.CreatedAt)).ToListAsync(cancellationToken);
    }

    public Task<Order?> GetOrderAsync(Guid id, CancellationToken cancellationToken = default) => db.Orders
        .AsNoTracking()
        .Include(order => order.Items)
        .Include(order => order.StatusHistory)
        .FirstOrDefaultAsync(order => order.Id == id, cancellationToken);

    public async Task<IReadOnlyList<Order>> GetCustomerOrdersAsync(Guid userId, Guid organizationId, CancellationToken cancellationToken = default) => await db.Orders
        .AsNoTracking()
        .Include(order => order.Items)
        .Include(order => order.StatusHistory)
        .Where(order => order.UserId == userId && db.Stores.Any(store => store.Id == order.StoreId && store.OrganizationId == organizationId))
        .ToListAsync(cancellationToken);

    public Task<Order?> GetCustomerOrderAsync(Guid orderId, Guid userId, Guid organizationId, CancellationToken cancellationToken = default) => db.Orders
        .AsNoTracking()
        .Include(order => order.Items)
        .Include(order => order.StatusHistory)
        .FirstOrDefaultAsync(order => order.Id == orderId && order.UserId == userId &&
            db.Stores.Any(store => store.Id == order.StoreId && store.OrganizationId == organizationId), cancellationToken);

    public async Task<IReadOnlyList<Notification>> GetNotificationsAsync(Guid customerId, bool unreadOnly, CancellationToken cancellationToken = default)
    {
        var query = db.Notifications.AsNoTracking().Where(notification => notification.CustomerId == customerId);
        if (unreadOnly)
        {
            query = query.Where(notification => !notification.IsRead);
        }

        return await query.ToListAsync(cancellationToken);
    }

    public async Task<bool> SetNotificationReadAsync(Guid notificationId, Guid customerId, bool isRead, CancellationToken cancellationToken = default)
    {
        var notification = await db.Notifications.FirstOrDefaultAsync(item => item.Id == notificationId && item.CustomerId == customerId, cancellationToken);
        if (notification is null)
        {
            return false;
        }

        notification.IsRead = isRead;
        await db.SaveChangesAsync(cancellationToken);
        return true;
    }

    public Task<Customer?> GetCustomerByUserIdAsync(Guid userId, CancellationToken cancellationToken = default) => db.Customers
        .AsNoTracking()
        .FirstOrDefaultAsync(customer => customer.UserId == userId && customer.IsActive, cancellationToken);

    public Task<Store?> GetStoreAsync(Guid id, CancellationToken cancellationToken = default) => db.Stores
        .AsNoTracking()
        .FirstOrDefaultAsync(store => store.Id == id, cancellationToken);

    public Task<Cart?> GetCartAsync(Guid customerId, Guid storeId, CancellationToken cancellationToken = default) => db.Carts
        .Include(cart => cart.Items)
        .AsNoTracking()
        .FirstOrDefaultAsync(cart => cart.CustomerId == customerId && cart.StoreId == storeId, cancellationToken);

    public async Task AddCartAsync(Cart cart, CancellationToken cancellationToken = default)
    {
        db.Carts.Add(cart);
        await db.SaveChangesAsync(cancellationToken);
    }

    public async Task SaveCartAsync(Cart cart, CancellationToken cancellationToken = default)
    {
        db.Carts.Update(cart);
        await db.SaveChangesAsync(cancellationToken);
    }

    private const int ConcurrencyAttempts = 4;
    private bool concurrencyLost;

    // Two customers buying the last units of a pack at the same moment both read the same stock row; the second SaveChanges fails
    // the row-version check. The stock may well still be enough, so the work is repeated on fresh data before a conflict is reported.
    // A real shortage is not retried. Nothing is oversold either way: the check is what stops it.
    public async Task<CheckoutCommitResult> TryCheckoutCartAsync(Guid customerId, Guid storeId, CheckoutRequest request, CancellationToken cancellationToken = default)
    {
        for (var attempt = 1; ; attempt++)
        {
            concurrencyLost = false;
            var result = await TryCheckoutCartOnceAsync(customerId, storeId, request, cancellationToken);
            if (!concurrencyLost || attempt >= ConcurrencyAttempts)
            {
                return result;
            }

            db.ChangeTracker.Clear();
        }
    }

    private async Task<CheckoutCommitResult> TryCheckoutCartOnceAsync(Guid customerId, Guid storeId, CheckoutRequest request, CancellationToken cancellationToken)
    {
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        try
        {
            var cart = await db.Carts
                .Include(currentCart => currentCart.Items)
                .SingleOrDefaultAsync(currentCart => currentCart.CustomerId == customerId && currentCart.StoreId == storeId, cancellationToken);
            if (cart is null)
            {
                await transaction.RollbackAsync(cancellationToken);
                return new CheckoutCommitResult(CheckoutCommitStatus.CartNotFound);
            }

            if (cart.Items.Count == 0)
            {
                await transaction.RollbackAsync(cancellationToken);
                return new CheckoutCommitResult(CheckoutCommitStatus.CartEmpty);
            }

            var userId = await db.Customers
                .Where(customer => customer.Id == customerId)
                .Select(customer => customer.UserId)
                .SingleAsync(cancellationToken);

            var orderItems = new List<OrderItem>();
            foreach (var cartItem in cart.Items)
            {
                var product = await db.Products.SingleOrDefaultAsync(item => item.Id == cartItem.ProductId, cancellationToken);
                var variant = await db.ProductVariants.SingleOrDefaultAsync(item => item.Id == cartItem.VariantId && item.ProductId == cartItem.ProductId, cancellationToken);
                if (product is null || !product.IsActive || variant is null || !variant.IsActive)
                {
                    await transaction.RollbackAsync(cancellationToken);
                    return new CheckoutCommitResult(CheckoutCommitStatus.ProductUnavailable);
                }

                // The price is the variant's, re-read here: the cart only holds what the customer saw.
                if (variant.Price != cartItem.UnitPriceSnapshot)
                {
                    await transaction.RollbackAsync(cancellationToken);
                    return new CheckoutCommitResult(CheckoutCommitStatus.PriceChanged);
                }

                var inventory = await db.StoreVariantInventory.SingleOrDefaultAsync(item => item.StoreId == storeId && item.VariantId == cartItem.VariantId, cancellationToken);
                if (inventory is null || inventory.AvailableQuantity < cartItem.Quantity)
                {
                    await transaction.RollbackAsync(cancellationToken);
                    return new CheckoutCommitResult(CheckoutCommitStatus.InventoryConflict);
                }

                inventory.AvailableQuantity -= cartItem.Quantity;
                // Snapshots: later price or label edits never change this order.
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

            var order = new Order
            {
                OrderNumber = $"ORD-{DateTime.UtcNow:yyyyMMddHHmmss}-{Guid.NewGuid():N}"[..32],
                UserId = userId,
                StoreId = storeId,
                DeliveryAddress = request.DeliveryAddress,
                Latitude = request.Latitude,
                Longitude = request.Longitude,
                Items = orderItems
            };
            order.TotalAmount = orderItems.Sum(item => item.TotalPrice);
            order.StatusHistory.Add(new OrderStatusHistory { Status = OrderStatus.Pending });

            db.Orders.Add(order);
            await db.SaveChangesAsync(cancellationToken);

            db.CartItems.RemoveRange(cart.Items);
            await db.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            return new CheckoutCommitResult(CheckoutCommitStatus.Succeeded, MapOrder(order));
        }
        catch (DbUpdateConcurrencyException)
        {
            concurrencyLost = true;
            await transaction.RollbackAsync(cancellationToken);
            return new CheckoutCommitResult(CheckoutCommitStatus.InventoryConflict);
        }
        catch (DbUpdateException)
        {
            await transaction.RollbackAsync(cancellationToken);
            return new CheckoutCommitResult(CheckoutCommitStatus.InventoryConflict);
        }
    }

    public async Task<OrderLifecycleResult> TryTransitionOrderAsync(Guid orderId, Guid organizationId, Guid? storeId, OrderStatus targetStatus, CancellationToken cancellationToken = default)
    {
        var order = await db.Orders
            .Include(currentOrder => currentOrder.Items)
            .Include(currentOrder => currentOrder.StatusHistory)
            .SingleOrDefaultAsync(currentOrder => currentOrder.Id == orderId &&
                (!storeId.HasValue || currentOrder.StoreId == storeId.Value) &&
                db.Stores.Any(store => store.Id == currentOrder.StoreId && store.OrganizationId == organizationId), cancellationToken);
        if (order is null)
        {
            return new OrderLifecycleResult(OrderLifecycleStatus.NotFound);
        }

        if (!OrderStatusTransitions.IsValid(order.Status, targetStatus))
        {
            return new OrderLifecycleResult(OrderLifecycleStatus.InvalidTransition);
        }

        order.Status = targetStatus;
        order.StatusHistory.Add(new OrderStatusHistory { Status = targetStatus });
        var customerId = await db.Customers
            .Where(customer => customer.UserId == order.UserId)
            .Select(customer => (Guid?)customer.Id)
            .SingleOrDefaultAsync(cancellationToken);
        if (customerId.HasValue)
        {
            db.Notifications.Add(CreateStatusNotification(customerId.Value, order.Id, targetStatus));
        }
        try
        {
            await db.SaveChangesAsync(cancellationToken);
            return new OrderLifecycleResult(OrderLifecycleStatus.Succeeded, MapOrder(order));
        }
        catch (DbUpdateConcurrencyException)
        {
            return new OrderLifecycleResult(OrderLifecycleStatus.ConcurrencyConflict);
        }
    }

    public Task<UserContext?> GetUserContextAsync(string externalSubject, CancellationToken cancellationToken = default) => db.Users
        .AsNoTracking()
        .Where(user => (user.ExternalSubject == externalSubject || user.Id.ToString() == externalSubject) && user.IsActive)
        .Select(user => new UserContext(user.Id, user.OrganizationId, user.StoreId, user.Role, user.StaffCategory))
        .FirstOrDefaultAsync(cancellationToken);

    public async Task<bool> TryCreateOrderAsync(Order order, IReadOnlyCollection<InventoryAdjustment> inventoryAdjustments, CancellationToken cancellationToken = default)
    {
        for (var attempt = 1; ; attempt++)
        {
            concurrencyLost = false;
            var created = await TryCreateOrderOnceAsync(order, inventoryAdjustments, cancellationToken);
            if (created || !concurrencyLost || attempt >= ConcurrencyAttempts)
            {
                return created;
            }

            db.ChangeTracker.Clear();
        }
    }

    private async Task<bool> TryCreateOrderOnceAsync(Order order, IReadOnlyCollection<InventoryAdjustment> inventoryAdjustments, CancellationToken cancellationToken)
    {
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        try
        {
            foreach (var adjustment in inventoryAdjustments)
            {
                var inventory = await db.StoreVariantInventory.SingleOrDefaultAsync(item => item.StoreId == adjustment.StoreId && item.VariantId == adjustment.VariantId, cancellationToken);
                if (inventory is null || inventory.AvailableQuantity < adjustment.Quantity)
                {
                    await transaction.RollbackAsync(cancellationToken);
                    return false;
                }

                inventory.AvailableQuantity -= adjustment.Quantity;
            }

            db.Orders.Add(order);
            await db.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            return true;
        }
        catch (DbUpdateConcurrencyException)
        {
            concurrencyLost = true;
            await transaction.RollbackAsync(cancellationToken);
            return false;
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
        order.StatusHistory.Select(history => new OrderStatusHistoryResponse(history.Status, history.ChangedAt)).ToArray());

    private static Notification CreateStatusNotification(Guid customerId, Guid orderId, OrderStatus status) => new()
    {
        CustomerId = customerId,
        OrderId = orderId,
        Type = "OrderStatusChanged",
        Title = "Order status updated",
        Message = $"Your order is now {status}.",
        IsRead = false
    };
}
