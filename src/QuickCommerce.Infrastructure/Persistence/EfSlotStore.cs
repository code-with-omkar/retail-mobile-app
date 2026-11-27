using Microsoft.EntityFrameworkCore;
using QuickCommerce.Application.Interfaces;
using QuickCommerce.Application.Services;
using QuickCommerce.Domain;

namespace QuickCommerce.Infrastructure.Persistence;

/// <summary>Delivery slot settings and bookings on SQL Server.</summary>
public sealed class EfSlotStore(QuickCommerceDbContext db) : ISlotStore
{
    public Task<DeliverySlotSetting?> GetDefaultAsync(CancellationToken cancellationToken = default) =>
        db.DeliverySlotSettings.AsNoTracking().FirstOrDefaultAsync(row => row.StoreId == null, cancellationToken);

    public async Task<IReadOnlyDictionary<Guid, DeliverySlotSetting>> GetOverridesAsync(CancellationToken cancellationToken = default) =>
        await db.DeliverySlotSettings.AsNoTracking().Where(row => row.StoreId != null).ToDictionaryAsync(row => row.StoreId!.Value, cancellationToken);

    public async Task SetAsync(Guid? storeId, SlotSettings? settings, string? by, DateTime now, CancellationToken cancellationToken = default)
    {
        if (settings is null)
        {
            await db.DeliverySlotSettings.Where(row => row.StoreId == storeId).ExecuteDeleteAsync(cancellationToken);
            return;
        }

        for (var attempt = 0; attempt < 3; attempt++)
        {
            var updated = await db.DeliverySlotSettings.Where(row => row.StoreId == storeId).ExecuteUpdateAsync(set => set
                .SetProperty(row => row.Enabled, settings.Enabled)
                .SetProperty(row => row.OpenMinutes, settings.OpenMinutes)
                .SetProperty(row => row.CloseMinutes, settings.CloseMinutes)
                .SetProperty(row => row.WindowMinutes, settings.WindowMinutes)
                .SetProperty(row => row.Capacity, settings.Capacity)
                .SetProperty(row => row.CutoffMinutes, settings.CutoffMinutes)
                .SetProperty(row => row.DaysAhead, settings.DaysAhead)
                .SetProperty(row => row.AsapAllowed, settings.AsapAllowed)
                .SetProperty(row => row.UpdatedAt, now)
                .SetProperty(row => row.UpdatedBy, by), cancellationToken);
            if (updated > 0)
            {
                return;
            }

            try
            {
                db.DeliverySlotSettings.Add(new DeliverySlotSetting
                {
                    StoreId = storeId, Enabled = settings.Enabled, OpenMinutes = settings.OpenMinutes, CloseMinutes = settings.CloseMinutes, WindowMinutes = settings.WindowMinutes,
                    Capacity = settings.Capacity, CutoffMinutes = settings.CutoffMinutes, DaysAhead = settings.DaysAhead, AsapAllowed = settings.AsapAllowed, UpdatedAt = now, UpdatedBy = by,
                });
                await db.SaveChangesAsync(cancellationToken);
                db.ChangeTracker.Clear();
                return;
            }
            catch (DbUpdateException)
            {
                // Someone saved it at the same moment: the next round updates their row.
                db.ChangeTracker.Clear();
            }
        }
    }

    public async Task<IReadOnlyDictionary<DateTime, int>> GetBookedAsync(Guid storeId, DateTime fromUtc, DateTime toUtc, CancellationToken cancellationToken = default) =>
        await SlotUsage.BookedAsync(db, storeId, fromUtc, toUtc, cancellationToken);

    public async Task<IReadOnlyList<Store>> GetStoresAsync(Guid organizationId, CancellationToken cancellationToken = default) =>
        await db.Stores.AsNoTracking().Where(store => store.OrganizationId == organizationId && store.IsActive).ToListAsync(cancellationToken);
}
