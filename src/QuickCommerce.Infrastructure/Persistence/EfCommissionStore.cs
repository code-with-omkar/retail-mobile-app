using Microsoft.EntityFrameworkCore;
using QuickCommerce.Application.Interfaces;
using QuickCommerce.Domain;

namespace QuickCommerce.Infrastructure.Persistence;

/// <summary>Commission percents on SQL Server: the default and per-store settings.</summary>
public sealed class EfCommissionStore(QuickCommerceDbContext db) : ICommissionStore
{
    public async Task<decimal> GetDefaultAsync(CancellationToken cancellationToken = default) =>
        await db.CommissionRates.AsNoTracking().Where(rate => rate.StoreId == null).Select(rate => rate.Percent).FirstOrDefaultAsync(cancellationToken);

    public async Task<IReadOnlyDictionary<Guid, decimal>> GetOverridesAsync(CancellationToken cancellationToken = default) =>
        await db.CommissionRates.AsNoTracking().Where(rate => rate.StoreId != null).ToDictionaryAsync(rate => rate.StoreId!.Value, rate => rate.Percent, cancellationToken);

    public async Task SetAsync(Guid? storeId, decimal? percent, string? by, DateTime now, CancellationToken cancellationToken = default)
    {
        if (percent is null)
        {
            await db.CommissionRates.Where(rate => rate.StoreId == storeId).ExecuteDeleteAsync(cancellationToken);
            return;
        }

        for (var attempt = 0; attempt < 3; attempt++)
        {
            var updated = await db.CommissionRates.Where(rate => rate.StoreId == storeId)
                .ExecuteUpdateAsync(set => set.SetProperty(rate => rate.Percent, percent.Value).SetProperty(rate => rate.UpdatedAt, now).SetProperty(rate => rate.UpdatedBy, by), cancellationToken);
            if (updated > 0)
            {
                return;
            }

            try
            {
                db.CommissionRates.Add(new CommissionRate { StoreId = storeId, Percent = percent.Value, UpdatedAt = now, UpdatedBy = by });
                await db.SaveChangesAsync(cancellationToken);
                db.ChangeTracker.Clear();
                return;
            }
            catch (DbUpdateException)
            {
                // Someone set it at the same moment: the next round updates their row.
                db.ChangeTracker.Clear();
            }
        }
    }

    public async Task<IReadOnlyList<Store>> GetStoresAsync(Guid organizationId, CancellationToken cancellationToken = default) =>
        await db.Stores.AsNoTracking().Where(store => store.OrganizationId == organizationId && store.IsActive).ToListAsync(cancellationToken);
}
