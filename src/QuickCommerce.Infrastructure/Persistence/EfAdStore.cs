using Microsoft.EntityFrameworkCore;
using QuickCommerce.Application.Interfaces;
using QuickCommerce.Domain;

namespace QuickCommerce.Infrastructure.Persistence;

/// <summary>Ads on SQL Server.</summary>
public sealed class EfAdStore(QuickCommerceDbContext db) : IAdStore
{
    public async Task<IReadOnlyList<Ad>> ListAsync(Guid organizationId, CancellationToken cancellationToken = default) =>
        await db.Ads.AsNoTracking().Where(item => item.OrganizationId == organizationId).ToListAsync(cancellationToken);

    public Task<Ad?> GetAsync(Guid organizationId, Guid id, CancellationToken cancellationToken = default) =>
        db.Ads.SingleOrDefaultAsync(item => item.OrganizationId == organizationId && item.Id == id, cancellationToken);

    public async Task AddAsync(Ad ad, CancellationToken cancellationToken = default)
    {
        db.Ads.Add(ad);
        await db.SaveChangesAsync(cancellationToken);
    }

    public Task SaveAsync(Ad ad, CancellationToken cancellationToken = default) => db.SaveChangesAsync(cancellationToken);

    public async Task<bool> DeleteAsync(Guid organizationId, Guid id, CancellationToken cancellationToken = default) =>
        await db.Ads.Where(item => item.OrganizationId == organizationId && item.Id == id).ExecuteDeleteAsync(cancellationToken) > 0;

    public Task<bool> StoreBelongsAsync(Guid organizationId, Guid storeId, CancellationToken cancellationToken = default) =>
        db.Stores.AsNoTracking().AnyAsync(item => item.Id == storeId && item.OrganizationId == organizationId, cancellationToken);

    public async Task<IReadOnlyList<Ad>> LiveAsync(string placement, Guid? storeId, DateTime now, int take, CancellationToken cancellationToken = default) =>
        await db.Ads.AsNoTracking()
            .Where(item => item.Placement == placement && item.IsActive && item.StartsAt <= now && (item.EndsAt == null || item.EndsAt > now)
                && (item.StoreId == null || (storeId != null && item.StoreId == storeId)))
            .OrderBy(item => item.SortOrder).ThenByDescending(item => item.StartsAt)
            .Take(take)
            .ToListAsync(cancellationToken);

    public async Task<bool> CountViewAsync(Guid id, CancellationToken cancellationToken = default) =>
        await db.Ads.Where(item => item.Id == id).ExecuteUpdateAsync(set => set.SetProperty(item => item.ViewCount, item => item.ViewCount + 1), cancellationToken) > 0;

    public async Task<bool> CountTapAsync(Guid id, CancellationToken cancellationToken = default) =>
        await db.Ads.Where(item => item.Id == id).ExecuteUpdateAsync(set => set.SetProperty(item => item.ClickCount, item => item.ClickCount + 1), cancellationToken) > 0;
}
