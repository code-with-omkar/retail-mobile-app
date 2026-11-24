using Microsoft.EntityFrameworkCore;
using QuickCommerce.Application.Interfaces;
using QuickCommerce.Domain;

namespace QuickCommerce.Infrastructure.Persistence;

/// <summary>Ratings for the order list, read in two queries for the whole list.</summary>
public sealed class EfOrderRatingLookup(QuickCommerceDbContext db) : IOrderRatingLookup
{
    public async Task<IReadOnlyDictionary<Guid, OrderRating>> ForOrdersAsync(IReadOnlyCollection<Guid> orderIds, CancellationToken cancellationToken = default) =>
        orderIds.Count == 0
            ? new Dictionary<Guid, OrderRating>()
            : await db.OrderRatings.AsNoTracking().Where(item => orderIds.Contains(item.OrderId)).ToDictionaryAsync(item => item.OrderId, cancellationToken);

    public async Task<IReadOnlyDictionary<Guid, (decimal Average, int Count)>> StoreAveragesAsync(IReadOnlyCollection<Guid> storeIds, CancellationToken cancellationToken = default)
    {
        if (storeIds.Count == 0)
        {
            return new Dictionary<Guid, (decimal, int)>();
        }

        var rows = await db.OrderRatings.AsNoTracking()
            .Where(item => storeIds.Contains(item.StoreId))
            .GroupBy(item => item.StoreId)
            .Select(group => new { group.Key, Average = group.Average(item => (decimal)item.StoreStars), Count = group.Count() })
            .ToListAsync(cancellationToken);
        return rows.ToDictionary(row => row.Key, row => (Math.Round(row.Average, 1), row.Count));
    }
}
