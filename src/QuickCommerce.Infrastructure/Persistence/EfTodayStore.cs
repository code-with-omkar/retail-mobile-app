using Microsoft.EntityFrameworkCore;
using QuickCommerce.Application.Interfaces;
using QuickCommerce.Domain;

namespace QuickCommerce.Infrastructure.Persistence;

/// <summary>The data behind a store's Today screen, on SQL Server. Read only.</summary>
public sealed class EfTodayStore(QuickCommerceDbContext db) : ITodayStore
{
    private static readonly OrderStatus[] OpenStatuses =
        [OrderStatus.Pending, OrderStatus.Accepted, OrderStatus.Preparing, OrderStatus.Ready, OrderStatus.OutForDelivery, OrderStatus.Confirmed];

    public Task<Store?> GetStoreAsync(Guid organizationId, Guid storeId, CancellationToken cancellationToken = default) =>
        db.Stores.AsNoTracking().FirstOrDefaultAsync(store => store.Id == storeId && store.OrganizationId == organizationId && store.IsActive, cancellationToken);

    public async Task<IReadOnlyList<TodayOrderRow>> GetOrderRowsAsync(Guid storeId, DateTime sinceUtc, CancellationToken cancellationToken = default) =>
        await db.Orders.AsNoTracking()
            .Where(order => order.StoreId == storeId && order.Status != OrderStatus.AwaitingPayment && (order.CreatedAt >= sinceUtc || OpenStatuses.Contains(order.Status)))
            .Select(order => new TodayOrderRow(
                order.Id,
                order.OrderNumber,
                order.Status,
                order.CreatedAt,
                // An online order becomes a new order for the store when it is paid, so its wait starts then.
                order.StatusHistory.Where(history => history.Status == OrderStatus.Pending).Max(history => (DateTime?)history.ChangedAt) ?? order.CreatedAt,
                order.StatusHistory.Where(history => history.Status == OrderStatus.Ready).Max(history => (DateTime?)history.ChangedAt),
                order.DeliveryPartnerId != null,
                order.PickedUpAt,
                order.TotalAmount,
                order.DeliverySlotStart,
                order.DeliverySlotEnd))
            .ToListAsync(cancellationToken);

    public async Task<IReadOnlyList<TodayStockRow>> GetLowStockAsync(Guid storeId, int take, CancellationToken cancellationToken = default) =>
        await (from stock in db.StoreVariantInventory.AsNoTracking()
               join variant in db.ProductVariants.AsNoTracking() on stock.VariantId equals variant.Id
               join product in db.Products.AsNoTracking() on variant.ProductId equals product.Id
               where stock.StoreId == storeId && variant.IsActive && product.IsActive && stock.AvailableQuantity <= stock.ReorderThreshold
               orderby stock.AvailableQuantity, product.Name
               select new TodayStockRow(product.Name, variant.Label, stock.AvailableQuantity, stock.ReorderThreshold))
            .Take(take)
            .ToListAsync(cancellationToken);
}
