using Microsoft.EntityFrameworkCore;
using QuickCommerce.Application.Interfaces;
using QuickCommerce.Domain;

namespace QuickCommerce.Infrastructure.Persistence;

/// <summary>The report queries on SQL Server: read-only, grouped in the database, and always limited to the organization and to the stores asked for.</summary>
public sealed class EfReportStore(QuickCommerceDbContext db) : IReportStore
{
    private IQueryable<Order> OrdersIn(Guid organizationId, IReadOnlyCollection<Guid>? storeIds) =>
        db.Orders.AsNoTracking().Where(order =>
            db.Stores.Any(store => store.Id == order.StoreId && store.OrganizationId == organizationId) &&
            (storeIds == null || storeIds.Contains(order.StoreId)));

    public async Task<IReadOnlyList<ReportOrderRow>> GetOrderRowsAsync(Guid organizationId, IReadOnlyCollection<Guid>? storeIds, DateTime fromUtc, DateTime toUtc, CancellationToken cancellationToken = default) =>
        await OrdersIn(organizationId, storeIds)
            .Where(order => order.CreatedAt >= fromUtc && order.CreatedAt < toUtc)
            .Select(order => new ReportOrderRow(order.Id, order.OrderNumber, order.StoreId, order.UserId, order.CreatedAt, order.Status, order.PaymentMethod, order.PaymentStatus, order.SubtotalAmount, order.DeliveryFee, order.HandlingFee, order.TotalAmount, order.CommissionAmount))
            .ToListAsync(cancellationToken);

    public async Task<IReadOnlyList<ReportProduct>> GetTopProductsAsync(Guid organizationId, IReadOnlyCollection<Guid>? storeIds, DateTime fromUtc, DateTime toUtc, int take, CancellationToken cancellationToken = default)
    {
        var rows = await OrdersIn(organizationId, storeIds)
            .Where(order => order.CreatedAt >= fromUtc && order.CreatedAt < toUtc && (order.Status == OrderStatus.Delivered || order.Status == OrderStatus.Completed))
            .SelectMany(order => order.Items)
            .GroupBy(item => item.ProductId)
            .Select(group => new { ProductId = group.Key, Name = group.Max(item => item.ProductNameSnapshot), Quantity = group.Sum(item => item.Quantity), Revenue = group.Sum(item => item.UnitPrice * item.Quantity) })
            .OrderByDescending(row => row.Revenue)
            .ThenBy(row => row.Name)
            .Take(take)
            .ToListAsync(cancellationToken);
        return rows.Select(row => new ReportProduct(row.ProductId, row.Name, row.Quantity, row.Revenue)).ToArray();
    }

    public Task<int> CountNewCustomersAsync(Guid organizationId, IReadOnlyCollection<Guid>? storeIds, DateTime fromUtc, DateTime toUtc, CancellationToken cancellationToken = default) =>
        OrdersIn(organizationId, storeIds)
            .Where(order => order.Status != OrderStatus.AwaitingPayment)
            .GroupBy(order => order.UserId)
            .Where(group => group.Min(order => order.CreatedAt) >= fromUtc && group.Min(order => order.CreatedAt) < toUtc)
            .CountAsync(cancellationToken);

    public async Task<IReadOnlyList<LowStockRow>> GetLowStockAsync(Guid organizationId, IReadOnlyCollection<Guid>? storeIds, int take, CancellationToken cancellationToken = default) =>
        await (from stock in db.StoreVariantInventory.AsNoTracking()
               join variant in db.ProductVariants.AsNoTracking() on stock.VariantId equals variant.Id
               join product in db.Products.AsNoTracking() on variant.ProductId equals product.Id
               join store in db.Stores.AsNoTracking() on stock.StoreId equals store.Id
               where store.OrganizationId == organizationId && store.IsActive && variant.IsActive && product.IsActive &&
                     (storeIds == null || storeIds.Contains(store.Id)) && stock.AvailableQuantity <= stock.ReorderThreshold
               orderby stock.AvailableQuantity, product.Name
               select new LowStockRow(store.Id, store.Name, product.Name, variant.Label, stock.AvailableQuantity, stock.ReorderThreshold))
            .Take(take)
            .ToListAsync(cancellationToken);

    public async Task<PlatformIncome> GetIncomeAsync(Guid organizationId, DateTime fromUtc, DateTime toUtc, CancellationToken cancellationToken = default)
    {
        var offers = await db.Campaigns.AsNoTracking()
            .Where(offer => offer.CreatedAt >= fromUtc && offer.CreatedAt < toUtc && offer.FeePaise > 0)
            .GroupBy(offer => offer.PaymentStatus)
            .Select(group => new { Status = group.Key, Paise = group.Sum(offer => (long)offer.FeePaise) })
            .ToListAsync(cancellationToken);
        var bids = await db.PlacementBids.AsNoTracking()
            .Where(bid => bid.CreatedAt >= fromUtc && bid.CreatedAt < toUtc && db.Stores.Any(store => store.Id == bid.StoreId && store.OrganizationId == organizationId))
            .GroupBy(bid => bid.PaymentStatus)
            .Select(group => new { Status = group.Key, Paise = group.Sum(bid => bid.AmountPaise) })
            .ToListAsync(cancellationToken);
        static decimal Rupees(IEnumerable<(PaymentState Status, long Paise)> rows, params PaymentState[] statuses) => rows.Where(row => statuses.Contains(row.Status)).Sum(row => row.Paise) / 100m;
        var offerRows = offers.Select(row => (row.Status, row.Paise)).ToArray();
        var bidRows = bids.Select(row => (row.Status, row.Paise)).ToArray();
        return new PlatformIncome(
            Rupees(offerRows, PaymentState.Paid),
            Rupees(offerRows, PaymentState.Refunded, PaymentState.Refunding),
            Rupees(bidRows, PaymentState.Paid),
            Rupees(bidRows, PaymentState.Refunded, PaymentState.Refunding));
    }

    public async Task<IReadOnlyDictionary<Guid, string>> GetStoreNamesAsync(Guid organizationId, IReadOnlyCollection<Guid>? storeIds, CancellationToken cancellationToken = default) =>
        await db.Stores.AsNoTracking()
            .Where(store => store.OrganizationId == organizationId && store.IsActive && (storeIds == null || storeIds.Contains(store.Id)))
            .ToDictionaryAsync(store => store.Id, store => store.Name, cancellationToken);
}
