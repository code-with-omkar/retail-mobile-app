using QuickCommerce.Application.Interfaces;
using QuickCommerce.Domain;

namespace QuickCommerce.Application.Services;

/// <summary>The Today screen of one store: counts, what needs attention, and the scheduled orders coming up.</summary>
public sealed class TodayService(
    ITodayStore store,
    ISlotStore slots,
    ICurrentUserContextResolver userContext,
    TimeProvider? timeProvider = null) : ITodayService
{
    private readonly TimeProvider clock = timeProvider ?? TimeProvider.System;

    public async Task<TodayResult> GetAsync(Guid? storeId, CancellationToken cancellationToken = default)
    {
        var context = await userContext.ResolveAsync(cancellationToken);
        if (context is null || context.Role is not (Role.StoreStaff or Role.Admin or Role.ApplicationAdmin))
        {
            return new(TodayStatus.Forbidden, Message: "The Today screen is for store staff and administrators.");
        }

        Guid chosen;
        if (context.Role == Role.StoreStaff)
        {
            // Staff see their own store only, whatever they ask for.
            if (context.StoreId is not { } own)
            {
                return new(TodayStatus.Forbidden, Message: "Store staff must be assigned to a store.");
            }

            chosen = own;
        }
        else if (storeId is { } named)
        {
            chosen = named;
        }
        else
        {
            return new(TodayStatus.Invalid, Message: "Choose a store.");
        }

        var found = await store.GetStoreAsync(context.OrganizationId, chosen, cancellationToken);
        if (found is null)
        {
            return new(TodayStatus.NotFound, Message: "Store not found.");
        }

        var now = clock.GetUtcNow().UtcDateTime;
        var dayStart = DateOnly.FromDateTime(now + SlotRules.India).ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc) - SlotRules.India;
        var settings = await SlotService.EffectiveAsync(slots, chosen, cancellationToken);
        var booked = settings.Enabled ? await slots.GetBookedAsync(chosen, now, now.AddDays(settings.DaysAhead + 1), cancellationToken) : new Dictionary<DateTime, int>();
        var orders = await store.GetOrderRowsAsync(chosen, dayStart, cancellationToken);
        var stock = await store.GetLowStockAsync(chosen, 200, cancellationToken);
        return new(TodayStatus.Succeeded, TodayRules.Build(chosen, found.Name, now, orders, stock, settings, booked));
    }
}
