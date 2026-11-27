using QuickCommerce.Application.DTOs;
using QuickCommerce.Application.Interfaces;
using QuickCommerce.Domain;

namespace QuickCommerce.Application.Services;

/// <summary>Something in the portal that is about to end, or has just ended.</summary>
/// <param name="Id">The same for as long as the thing and its state are the same, so a screen can remember what was read.</param>
/// <param name="Kind">Coupon, Offer or Ad.</param>
/// <param name="Stores">The stores it is for; empty means every store.</param>
public sealed record PortalAlert(string Id, string Kind, Guid ItemId, string Name, DateTime EndsAt, bool Ended, IReadOnlyList<string> Stores);

public interface IPortalAlertService
{
    /// <summary>
    /// Administrators: coupons, offers and ads. Store managers: the offers and ads of their stores (and those for every store).
    /// Null for anyone else.
    /// </summary>
    Task<IReadOnlyList<PortalAlert>?> GetAsync(CancellationToken cancellationToken = default);
}

/// <summary>Worked out from what is saved each time it is asked, so nothing has to be scheduled and a changed end date is right at once.</summary>
public sealed class PortalAlertService(
    ICouponStore coupons,
    ICampaignStore campaigns,
    IAdStore ads,
    IAuthorizationScopeService scopeService,
    ICurrentUserContextResolver userContext,
    TimeProvider? timeProvider = null) : IPortalAlertService
{
    /// <summary>How long before the end the alert appears, and how long it stays after.</summary>
    public static readonly TimeSpan Window = TimeSpan.FromDays(3);
    private readonly TimeProvider clock = timeProvider ?? TimeProvider.System;

    public async Task<IReadOnlyList<PortalAlert>?> GetAsync(CancellationToken cancellationToken = default)
    {
        var scope = await scopeService.ResolveAsync(cancellationToken);
        if (scope is null)
        {
            return null;
        }

        var admin = scope.IsApplicationAdmin;
        IReadOnlySet<Guid>? mine = null;
        if (!admin)
        {
            var context = await userContext.ResolveAsync(cancellationToken);
            if (context is not { Role: Role.StoreStaff, StaffCategory: StaffCategory.StoreManager })
            {
                return null;
            }

            mine = context.OrderStoreIds;
        }

        var now = clock.GetUtcNow().UtcDateTime;
        var found = new List<PortalAlert>();
        void Add(string kind, Guid id, string name, DateTime ends, IReadOnlyList<string> stores)
        {
            var ended = ends <= now;
            found.Add(new PortalAlert($"{kind}:{id}:{(ended ? "ended" : "ending")}:{ends.Ticks}", kind, id, name, DateTime.SpecifyKind(ends, DateTimeKind.Utc), ended, stores));
        }

        bool Near(DateTime? ends) => ends is { } at && at > now - Window && at <= now + Window;

        if (admin)
        {
            foreach (var item in await coupons.ListAsync(scope.OrganizationId, cancellationToken))
            {
                if (item.Coupon.IsActive && Near(item.Coupon.EndsAt))
                {
                    Add("Coupon", item.Coupon.Id, item.Coupon.Code, item.Coupon.EndsAt!.Value, []);
                }
            }
        }

        foreach (var offer in await campaigns.ListCampaignsAsync(cancellationToken))
        {
            if (offer.IsActive && offer.PaymentStatus is PaymentState.NotRequired or PaymentState.Paid && Near(offer.EndsAt)
                && (mine is null || offer.Stores.Count == 0 || offer.Stores.Any(link => mine.Contains(link.StoreId))))
            {
                // A manager is told only about their own stores' part in it.
                Add("Offer", offer.Id, offer.TitleEn, offer.EndsAt!.Value,
                    offer.Stores.Where(link => mine is null || mine.Contains(link.StoreId)).Select(link => link.Store?.Name ?? string.Empty).Order().ToArray());
            }
        }

        var ending = (await ads.ListAsync(scope.OrganizationId, cancellationToken))
            .Where(ad => ad.IsActive && Near(ad.EndsAt) && (mine is null || ad.StoreId is null || mine.Contains(ad.StoreId.Value)))
            .ToArray();
        var storeIds = ending.Where(ad => ad.StoreId.HasValue).Select(ad => ad.StoreId!.Value).Distinct().ToArray();
        var names = storeIds.Length == 0
            ? []
            : (await campaigns.GetStoresAsync(storeIds, scope.OrganizationId, cancellationToken)).ToDictionary(store => store.Id, store => store.Name);
        foreach (var ad in ending)
        {
            Add("Ad", ad.Id, string.IsNullOrWhiteSpace(ad.Title) ? "Ad without a title" : ad.Title, ad.EndsAt!.Value,
                ad.StoreId is { } storeId ? [names.GetValueOrDefault(storeId) ?? string.Empty] : []);
        }

        // Still to end first (soonest first), then the ones that ended (latest first).
        return found.OrderBy(alert => alert.Ended).ThenBy(alert => alert.Ended ? -alert.EndsAt.Ticks : alert.EndsAt.Ticks).ToArray();
    }
}
