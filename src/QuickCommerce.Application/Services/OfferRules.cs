using QuickCommerce.Application.Interfaces;
using QuickCommerce.Domain;

namespace QuickCommerce.Application.Services;

/// <summary>
/// When an offer's discount is on and when its banner shows. Pure rules, shared by the SQL store and the in-memory store so they cannot
/// disagree (the SQL store also writes the discount rule as a query, and the parity tests keep the two honest).
/// </summary>
public static class OfferRules
{
    /// <summary>How long a picture-only message offer stays on Home after it starts.</summary>
    public const int MessageBannerDays = 7;

    private static bool Paid(Campaign campaign) => campaign.PaymentStatus is PaymentState.NotRequired or PaymentState.Paid;

    /// <summary>Whether the offer is for this store: an offer naming no store is for every store.</summary>
    public static bool AppliesToStore(Campaign campaign, Guid storeId) => campaign.Stores.Count == 0 || campaign.Stores.Any(link => link.StoreId == storeId);

    /// <summary>An offer a customer may open: started, not cancelled and (with a fee) paid.</summary>
    public static bool IsOpenable(Campaign campaign, DateTime now) => campaign.IsActive && Paid(campaign) && campaign.StartsAt <= now;

    public static OfferDetail Detail(Campaign campaign, DateTime now) => new(
        campaign.Id,
        campaign.TitleEn,
        string.IsNullOrEmpty(campaign.BodyEn) ? null : campaign.BodyEn,
        campaign.TitleMr,
        campaign.BodyMr,
        campaign.ImageUrl,
        campaign.DiscountPercent,
        campaign.EndsAt is { } ends ? DateTime.SpecifyKind(ends, DateTimeKind.Utc) : null,
        DiscountRunning(campaign, now),
        campaign.Stores.Select(link => new CampaignStoreInfo(link.StoreId, link.Store?.Name ?? string.Empty)).OrderBy(info => info.Name).ToArray(),
        campaign.Products.Select(link => link.ProductId).ToArray());

    /// <summary>The discount is on: not cancelled, paid (when it has a fee) and between its start and its end.</summary>
    public static bool DiscountRunning(Campaign campaign, DateTime now) =>
        campaign.IsActive && campaign.DiscountPercent > 0 && Paid(campaign) && campaign.StartsAt <= now && campaign.EndsAt > now;

    /// <summary>Shown as a banner on Home: a running discount, or a message with a picture for a week after it started.</summary>
    public static bool ShowsAsBanner(Campaign campaign, DateTime now) =>
        DiscountRunning(campaign, now)
        || (campaign.IsActive && campaign.DiscountPercent == 0 && campaign.ImageUrl is not null && Paid(campaign) && campaign.StartsAt <= now && campaign.StartsAt > now.AddDays(-MessageBannerDays));
}
