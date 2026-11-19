using Microsoft.EntityFrameworkCore;
using QuickCommerce.Domain;

namespace QuickCommerce.Infrastructure.Persistence;

/// <summary>Giving a coupon use back when its order never goes ahead (cancelled, rejected, or unpaid when the hold ran out).</summary>
internal static class CouponUsage
{
    /// <summary>
    /// Marks the order's coupon use as released and takes it off the coupon's count. Call it in the same transaction as the status change.
    /// Safe to call for an order without a coupon, and twice for the same order.
    /// </summary>
    public static async Task ReleaseAsync(QuickCommerceDbContext db, Guid orderId, DateTime now, CancellationToken cancellationToken)
    {
        var redemption = await db.CouponRedemptions.SingleOrDefaultAsync(item => item.OrderId == orderId && item.ReleasedAt == null, cancellationToken);
        if (redemption is null)
        {
            return;
        }

        redemption.ReleasedAt = now;
        await db.Database.ExecuteSqlInterpolatedAsync($"UPDATE Coupons SET UsedCount = UsedCount - 1 WHERE Id = {redemption.CouponId} AND UsedCount > 0", cancellationToken);
    }
}
