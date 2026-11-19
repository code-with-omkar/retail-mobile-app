using Microsoft.EntityFrameworkCore;
using QuickCommerce.Application.Services;
using QuickCommerce.Domain;

namespace QuickCommerce.Infrastructure.Persistence;

/// <summary>Scheduled-delivery windows on SQL Server: the settings in force for a store, and taking or giving back a place in a window.</summary>
internal static class SlotUsage
{
    /// <summary>The store's own settings, else the default, else scheduling off.</summary>
    public static async Task<SlotSettings> EffectiveAsync(QuickCommerceDbContext db, Guid storeId, CancellationToken cancellationToken)
    {
        var rows = await db.DeliverySlotSettings.AsNoTracking().Where(row => row.StoreId == storeId || row.StoreId == null).ToListAsync(cancellationToken);
        var row = rows.FirstOrDefault(item => item.StoreId == storeId) ?? rows.FirstOrDefault(item => item.StoreId == null);
        return row is null ? SlotSettings.Off : SlotSettings.From(row);
    }

    /// <summary>How many orders each window of the store holds, from <paramref name="fromUtc"/> up to <paramref name="toUtc"/>.</summary>
    public static async Task<Dictionary<DateTime, int>> BookedAsync(QuickCommerceDbContext db, Guid storeId, DateTime fromUtc, DateTime toUtc, CancellationToken cancellationToken) =>
        await db.SlotBookings.AsNoTracking().Where(row => row.StoreId == storeId && row.SlotStart >= fromUtc && row.SlotStart < toUtc)
            .ToDictionaryAsync(row => row.SlotStart, row => row.Booked, cancellationToken);

    /// <summary>
    /// Takes one place in the window with a single statement, so two customers racing for the last place cannot both get it.
    /// False when the window is full.
    /// </summary>
    public static async Task<bool> TakeAsync(QuickCommerceDbContext db, Guid storeId, DateTime slotStart, int capacity, CancellationToken cancellationToken)
    {
        var changed = await db.Database.ExecuteSqlInterpolatedAsync($"""
            MERGE SlotBookings WITH (HOLDLOCK) AS target
            USING (SELECT {storeId} AS StoreId, {slotStart} AS SlotStart) AS source ON target.StoreId = source.StoreId AND target.SlotStart = source.SlotStart
            WHEN MATCHED AND target.Booked < {capacity} THEN UPDATE SET Booked = target.Booked + 1
            WHEN NOT MATCHED THEN INSERT (StoreId, SlotStart, Booked) VALUES (source.StoreId, source.SlotStart, 1);
            """, cancellationToken);
        return changed > 0;
    }

    /// <summary>Gives the order's place back when it never goes ahead (cancelled, rejected, or unpaid when the hold ran out). Safe for an ASAP order.</summary>
    public static async Task ReleaseAsync(QuickCommerceDbContext db, Order order, CancellationToken cancellationToken)
    {
        if (order.DeliverySlotStart is not { } start)
        {
            return;
        }

        await db.Database.ExecuteSqlInterpolatedAsync($"UPDATE SlotBookings SET Booked = Booked - 1 WHERE StoreId = {order.StoreId} AND SlotStart = {start} AND Booked > 0", cancellationToken);
    }
}
