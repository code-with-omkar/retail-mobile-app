using Microsoft.EntityFrameworkCore;
using QuickCommerce.Application.Services;
using QuickCommerce.Domain;

namespace QuickCommerce.Infrastructure.Persistence;

/// <summary>The discount query, written once so the catalogue, the cart and checkout (inside its transaction) read the same thing.</summary>
internal static class OfferDiscounts
{
    public static async Task<IReadOnlyDictionary<Guid, int>> PercentsAsync(QuickCommerceDbContext db, Guid storeId, IReadOnlyCollection<Guid> productIds, DateTime now, CancellationToken cancellationToken)
    {
        if (productIds.Count == 0)
        {
            return new Dictionary<Guid, int>();
        }

        var rows = await db.Campaigns.AsNoTracking()
            .Where(campaign => campaign.IsActive && campaign.DiscountPercent > 0 && campaign.StartsAt <= now && campaign.EndsAt > now
                && (campaign.PaymentStatus == PaymentState.NotRequired || campaign.PaymentStatus == PaymentState.Paid)
                && (!campaign.Stores.Any() || campaign.Stores.Any(link => link.StoreId == storeId)))
            .SelectMany(campaign => campaign.Products.Where(link => productIds.Contains(link.ProductId)).Select(link => new { link.ProductId, campaign.DiscountPercent }))
            .ToListAsync(cancellationToken);
        return rows.GroupBy(row => row.ProductId).ToDictionary(group => group.Key, group => OfferPricing.Best(group.Select(row => row.DiscountPercent)));
    }
}
