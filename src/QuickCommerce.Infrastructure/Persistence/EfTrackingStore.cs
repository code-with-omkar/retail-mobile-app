using Microsoft.EntityFrameworkCore;
using QuickCommerce.Application.Interfaces;
using QuickCommerce.Domain;

namespace QuickCommerce.Infrastructure.Persistence;

/// <summary>The latest position of each delivery on SQL Server: one row per order, replaced on every update.</summary>
public sealed class EfTrackingStore(QuickCommerceDbContext db) : ITrackingStore
{
    public Task<TrackedOrder?> GetOrderAsync(Guid orderId, CancellationToken cancellationToken = default) =>
        db.Orders.AsNoTracking().Where(order => order.Id == orderId)
            .Select(order => new TrackedOrder(order.Id, order.UserId, order.DeliveryPartnerId, order.Status, order.Latitude, order.Longitude))
            .FirstOrDefaultAsync(cancellationToken);

    public async Task SaveLocationAsync(Guid orderId, Guid partnerId, double latitude, double longitude, DateTime now, CancellationToken cancellationToken = default)
    {
        await db.DeliveryLocations.Where(location => location.UpdatedAt < now.AddDays(-1)).ExecuteDeleteAsync(cancellationToken);
        for (var attempt = 0; attempt < 3; attempt++)
        {
            var updated = await db.DeliveryLocations.Where(location => location.OrderId == orderId)
                .ExecuteUpdateAsync(set => set.SetProperty(location => location.PartnerId, partnerId).SetProperty(location => location.Latitude, latitude).SetProperty(location => location.Longitude, longitude).SetProperty(location => location.UpdatedAt, now), cancellationToken);
            if (updated > 0)
            {
                return;
            }

            try
            {
                db.DeliveryLocations.Add(new DeliveryLocation { OrderId = orderId, PartnerId = partnerId, Latitude = latitude, Longitude = longitude, UpdatedAt = now });
                await db.SaveChangesAsync(cancellationToken);
                db.ChangeTracker.Clear();
                return;
            }
            catch (DbUpdateException)
            {
                // The first two updates arrived together: the next round replaces the row that won.
                db.ChangeTracker.Clear();
            }
        }
    }

    public Task<DeliveryLocation?> GetLocationAsync(Guid orderId, CancellationToken cancellationToken = default) =>
        db.DeliveryLocations.AsNoTracking().FirstOrDefaultAsync(location => location.OrderId == orderId, cancellationToken);

    public async Task DeleteLocationAsync(Guid orderId, CancellationToken cancellationToken = default) =>
        await db.DeliveryLocations.Where(location => location.OrderId == orderId).ExecuteDeleteAsync(cancellationToken);
}
