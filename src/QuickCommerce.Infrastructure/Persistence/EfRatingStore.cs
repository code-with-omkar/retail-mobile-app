using Microsoft.EntityFrameworkCore;
using QuickCommerce.Application.Interfaces;
using QuickCommerce.Domain;

namespace QuickCommerce.Infrastructure.Persistence;

/// <summary>Order ratings on SQL Server. One row per order (the order id is the key), and the figures are added up in the database.</summary>
public sealed class EfRatingStore(QuickCommerceDbContext db) : IRatingStore
{
    public Task<OrderRating?> GetAsync(Guid orderId, CancellationToken cancellationToken = default) =>
        db.OrderRatings.AsNoTracking().FirstOrDefaultAsync(item => item.OrderId == orderId, cancellationToken);

    public async Task SaveAsync(OrderRating rating, CancellationToken cancellationToken = default)
    {
        for (var attempt = 1; ; attempt++)
        {
            var existing = await db.OrderRatings.FirstOrDefaultAsync(item => item.OrderId == rating.OrderId, cancellationToken);
            if (existing is null)
            {
                db.OrderRatings.Add(new OrderRating
                {
                    OrderId = rating.OrderId,
                    StoreId = rating.StoreId,
                    CustomerId = rating.CustomerId,
                    DeliveryPartnerId = rating.DeliveryPartnerId,
                    StoreStars = rating.StoreStars,
                    DeliveryStars = rating.DeliveryStars,
                    Comment = rating.Comment,
                    CreatedAt = rating.CreatedAt,
                    UpdatedAt = rating.UpdatedAt
                });
            }
            else
            {
                existing.StoreStars = rating.StoreStars;
                existing.DeliveryStars = rating.DeliveryStars;
                existing.Comment = rating.Comment;
                existing.UpdatedAt = rating.UpdatedAt;
            }

            try
            {
                await db.SaveChangesAsync(cancellationToken);
                db.ChangeTracker.Clear();
                return;
            }
            catch (DbUpdateException) when (attempt < 3)
            {
                // Two taps at the same moment: the second one now finds the first one's row and updates it.
                db.ChangeTracker.Clear();
            }
        }
    }

    private IQueryable<OrderRating> In(Guid organizationId, IReadOnlyCollection<Guid>? storeIds, DateTime fromUtc, DateTime toUtc) =>
        db.OrderRatings.AsNoTracking().Where(rating =>
            rating.CreatedAt >= fromUtc && rating.CreatedAt < toUtc &&
            db.Stores.Any(store => store.Id == rating.StoreId && store.OrganizationId == organizationId) &&
            (storeIds == null || storeIds.Contains(rating.StoreId)));

    public async Task<RatingSummary> SummaryAsync(Guid organizationId, IReadOnlyCollection<Guid>? storeIds, DateTime fromUtc, DateTime toUtc, CancellationToken cancellationToken = default)
    {
        var rows = In(organizationId, storeIds, fromUtc, toUtc);
        var count = await rows.CountAsync(cancellationToken);
        if (count == 0)
        {
            return new RatingSummary(0, 0, null, [0, 0, 0, 0, 0], []);
        }

        var average = await rows.AverageAsync(rating => (double)rating.StoreStars, cancellationToken);
        var delivery = rows.Where(rating => rating.DeliveryStars != null);
        double? deliveryAverage = await delivery.AnyAsync(cancellationToken) ? await delivery.AverageAsync(rating => (double)rating.DeliveryStars!.Value, cancellationToken) : null;
        var stars = await rows.GroupBy(rating => rating.StoreStars).Select(group => new { Stars = group.Key, Count = group.Count() }).ToListAsync(cancellationToken);
        var partners = await (from rating in delivery.Where(item => item.DeliveryPartnerId != null)
                              join user in db.Users.AsNoTracking() on rating.DeliveryPartnerId equals user.Id
                              group rating by new { user.Id, user.DisplayName } into grouped
                              select new { grouped.Key.Id, grouped.Key.DisplayName, Count = grouped.Count(), Average = grouped.Average(item => (double)item.DeliveryStars!.Value) })
            .OrderByDescending(item => item.Average).ThenBy(item => item.DisplayName)
            .ToListAsync(cancellationToken);
        return new RatingSummary(
            count,
            Math.Round((decimal)average, 2),
            deliveryAverage is null ? null : Math.Round((decimal)deliveryAverage.Value, 2),
            Enumerable.Range(1, 5).Select(star => stars.FirstOrDefault(item => item.Stars == star)?.Count ?? 0).ToArray(),
            partners.Select(item => new PartnerRating(item.Id, item.DisplayName, item.Count, Math.Round((decimal)item.Average, 2))).ToArray());
    }

    public async Task<IReadOnlyList<RatingRow>> RecentAsync(Guid organizationId, IReadOnlyCollection<Guid>? storeIds, DateTime fromUtc, DateTime toUtc, int take, CancellationToken cancellationToken = default) =>
        await (from rating in In(organizationId, storeIds, fromUtc, toUtc)
               join order in db.Orders.AsNoTracking() on rating.OrderId equals order.Id
               join shop in db.Stores.AsNoTracking() on rating.StoreId equals shop.Id
               orderby rating.CreatedAt descending
               select new RatingRow(rating.OrderId, order.OrderNumber, shop.Name, rating.StoreStars, rating.DeliveryStars, rating.Comment, rating.CreatedAt))
            .Take(take)
            .ToListAsync(cancellationToken);

    public Task<bool> StoreExistsAsync(Guid organizationId, Guid storeId, CancellationToken cancellationToken = default) =>
        db.Stores.AsNoTracking().AnyAsync(store => store.Id == storeId && store.OrganizationId == organizationId && store.IsActive, cancellationToken);
}
