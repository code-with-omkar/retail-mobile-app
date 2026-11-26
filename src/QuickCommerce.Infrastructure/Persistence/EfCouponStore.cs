using Microsoft.EntityFrameworkCore;
using QuickCommerce.Application.Interfaces;
using QuickCommerce.Domain;

namespace QuickCommerce.Infrastructure.Persistence;

/// <summary>Coupons on SQL Server.</summary>
public sealed class EfCouponStore(QuickCommerceDbContext db) : ICouponStore
{
    public Task<Coupon?> GetByCodeAsync(Guid organizationId, string code, CancellationToken cancellationToken = default) =>
        db.Coupons.AsNoTracking().SingleOrDefaultAsync(item => item.OrganizationId == organizationId && item.Code == code, cancellationToken);

    public Task<Coupon?> GetAsync(Guid organizationId, Guid id, CancellationToken cancellationToken = default) =>
        db.Coupons.SingleOrDefaultAsync(item => item.OrganizationId == organizationId && item.Id == id, cancellationToken);

    public async Task<IReadOnlyList<CouponSummary>> ListAsync(Guid organizationId, CancellationToken cancellationToken = default)
    {
        var coupons = await db.Coupons.AsNoTracking().Where(item => item.OrganizationId == organizationId).ToListAsync(cancellationToken);
        var given = await db.CouponRedemptions.AsNoTracking()
            .Where(item => item.ReleasedAt == null && db.Coupons.Any(coupon => coupon.Id == item.CouponId && coupon.OrganizationId == organizationId))
            .GroupBy(item => item.CouponId)
            .Select(group => new { group.Key, Total = group.Sum(item => item.Amount) })
            .ToDictionaryAsync(item => item.Key, item => item.Total, cancellationToken);
        return coupons.Select(coupon => new CouponSummary(coupon, given.GetValueOrDefault(coupon.Id))).ToArray();
    }

    public async Task<bool> AddAsync(Coupon coupon, CancellationToken cancellationToken = default)
    {
        db.Coupons.Add(coupon);
        return await SaveOrCollisionAsync(coupon, cancellationToken);
    }

    public Task<bool> SaveAsync(Coupon coupon, CancellationToken cancellationToken = default) => SaveOrCollisionAsync(coupon, cancellationToken);

    /// <summary>The unique index on (organisation, code) is the judge, so two administrators typing the same code at once cannot both succeed.</summary>
    private async Task<bool> SaveOrCollisionAsync(Coupon coupon, CancellationToken cancellationToken)
    {
        try
        {
            await db.SaveChangesAsync(cancellationToken);
            return true;
        }
        catch (DbUpdateException ex) when (ex.InnerException is Microsoft.Data.SqlClient.SqlException { Number: 2601 or 2627 })
        {
            db.Entry(coupon).State = EntityState.Detached;
            return false;
        }
    }

    public Task<Guid?> GetStoreOrganizationAsync(Guid storeId, CancellationToken cancellationToken = default) =>
        db.Stores.AsNoTracking().Where(store => store.Id == storeId && store.IsActive).Select(store => (Guid?)store.OrganizationId).SingleOrDefaultAsync(cancellationToken);

    public async Task<IReadOnlyList<Coupon>> ListShownAsync(Guid organizationId, DateTime now, CancellationToken cancellationToken = default) =>
        await db.Coupons.AsNoTracking()
            .Where(item => item.OrganizationId == organizationId && item.IsActive && item.ShowInApp && item.StartsAt <= now
                && (item.EndsAt == null || item.EndsAt > now) && (item.TotalLimit == null || item.UsedCount < item.TotalLimit))
            .ToListAsync(cancellationToken);

    public Task<int> UsesByUserAsync(Guid couponId, Guid userId, CancellationToken cancellationToken = default) =>
        db.CouponRedemptions.AsNoTracking().CountAsync(item => item.CouponId == couponId && item.UserId == userId && item.ReleasedAt == null, cancellationToken);

    public Task<bool> HasOrdersAsync(Guid userId, CancellationToken cancellationToken = default) =>
        db.Orders.AsNoTracking().AnyAsync(item => item.UserId == userId && item.Status != OrderStatus.Cancelled && item.Status != OrderStatus.Rejected, cancellationToken);
}
