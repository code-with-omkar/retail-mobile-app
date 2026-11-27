using QuickCommerce.Domain;

namespace QuickCommerce.Application.Interfaces;

/// <summary>The counts for one store's day. New, Packing, Ready and OutForDelivery are everything open right now, whatever day it was placed; the rest are today's (India day).</summary>
public sealed record TodayCounts(int Placed, int New, int Packing, int Ready, int OutForDelivery, int Delivered, int Cancelled, decimal Revenue);

/// <param name="Kind">PendingTooLong, ReadyNotCollected, OutOfStock, LowStock or WindowsNearlyFull.</param>
/// <param name="Severity">"warning" or "urgent".</param>
public sealed record TodayAlert(string Kind, string Severity, string Title, string Detail, Guid? OrderId = null);

/// <summary>A scheduled order that is still open, for the list of what is coming.</summary>
public sealed record TodayUpcoming(Guid OrderId, string OrderNumber, DateTime StartUtc, DateTime EndUtc, string Label, string Status);

public sealed record TodaySummary(Guid StoreId, string StoreName, DateTime AsOf, TodayCounts Counts, IReadOnlyList<TodayAlert> Alerts, IReadOnlyList<TodayUpcoming> Upcoming);

/// <summary>One order as the Today rules see it.</summary>
/// <param name="PendingAt">When it became a new order for the store (for an online order, when it was paid).</param>
/// <param name="ReadyAt">When the store marked it ready; null if it is not.</param>
public sealed record TodayOrderRow(Guid Id, string OrderNumber, OrderStatus Status, DateTime CreatedAt, DateTime PendingAt, DateTime? ReadyAt, bool HasPartner, DateTime? PickedUpAt, decimal Total, DateTime? SlotStart, DateTime? SlotEnd);

public sealed record TodayStockRow(string Product, string Size, int Available, int Reorder);

public interface ITodayStore
{
    Task<Store?> GetStoreAsync(Guid organizationId, Guid storeId, CancellationToken cancellationToken = default);

    /// <summary>Orders placed since <paramref name="sinceUtc"/>, and every order still open whenever it was placed. Never an order awaiting payment.</summary>
    Task<IReadOnlyList<TodayOrderRow>> GetOrderRowsAsync(Guid storeId, DateTime sinceUtc, CancellationToken cancellationToken = default);

    /// <summary>Active items of the store at or below their reorder level (zero included), fewest first.</summary>
    Task<IReadOnlyList<TodayStockRow>> GetLowStockAsync(Guid storeId, int take, CancellationToken cancellationToken = default);
}

public enum TodayStatus { Succeeded, Forbidden, NotFound, Invalid }

public sealed record TodayResult(TodayStatus Status, TodaySummary? Value = null, string? Message = null);

public interface ITodayService
{
    /// <summary>Store staff get their own store; an administrator names the store.</summary>
    Task<TodayResult> GetAsync(Guid? storeId, CancellationToken cancellationToken = default);
}
