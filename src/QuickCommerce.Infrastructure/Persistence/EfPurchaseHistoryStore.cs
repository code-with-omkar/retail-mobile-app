using Microsoft.EntityFrameworkCore;
using QuickCommerce.Application.Interfaces;
using QuickCommerce.Domain;

namespace QuickCommerce.Infrastructure.Persistence;

/// <summary>Past purchases on SQL Server, read from the customer's own orders.</summary>
public sealed class EfPurchaseHistoryStore(QuickCommerceDbContext db) : IPurchaseHistoryStore
{
    private IQueryable<Order> Bought(Guid userId) =>
        db.Orders.AsNoTracking().Where(order => order.UserId == userId && order.Status != OrderStatus.Cancelled && order.Status != OrderStatus.AwaitingPayment);

    public async Task<IReadOnlyList<RecentStore>> RecentStoresAsync(Guid userId, int take, CancellationToken cancellationToken = default)
    {
        var latest = await Bought(userId)
            .GroupBy(order => order.StoreId)
            .Select(group => new { StoreId = group.Key, At = group.Max(order => order.CreatedAt) })
            .OrderByDescending(row => row.At)
            .Take(take + 6)
            .ToListAsync(cancellationToken);
        var ids = latest.Select(row => row.StoreId).ToArray();
        var stores = await db.Stores.AsNoTracking().Where(store => ids.Contains(store.Id) && store.IsActive).ToDictionaryAsync(store => store.Id, cancellationToken);
        return latest.Where(row => stores.ContainsKey(row.StoreId)).Take(take)
            .Select(row => new RecentStore(row.StoreId, stores[row.StoreId].Name, stores[row.StoreId].Address, DateTime.SpecifyKind(row.At, DateTimeKind.Utc)))
            .ToArray();
    }

    public async Task<IReadOnlyList<Guid>> RecentProductIdsAsync(Guid userId, Guid storeId, int take, CancellationToken cancellationToken = default)
    {
        var rows = await Bought(userId).Where(order => order.StoreId == storeId)
            .SelectMany(order => order.Items.Select(item => new { item.ProductId, order.CreatedAt }))
            .GroupBy(row => row.ProductId)
            .Select(group => new { ProductId = group.Key, At = group.Max(row => row.CreatedAt) })
            .OrderByDescending(row => row.At)
            .ThenBy(row => row.ProductId)
            .Take(take)
            .ToListAsync(cancellationToken);
        return rows.Select(row => row.ProductId).ToArray();
    }
}
