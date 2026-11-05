using QuickCommerce.Application.Interfaces;
using QuickCommerce.Domain;

namespace QuickCommerce.Application.Services;

/// <summary>
/// What a store's Today screen shows, worked out from the orders, the stock and the delivery windows. Pure rules, so every limit is tested
/// without a database. The limits are fixed here (and quoted on the screen); making them settings is a later step if stores ask.
/// </summary>
public static class TodayRules
{
    public const int NewOrderWarnMinutes = 5;
    public const int NewOrderUrgentMinutes = 10;
    public const int ReadyWarnMinutes = 20;
    public const int ReadyUrgentMinutes = 40;

    /// <summary>A window at or above this share of its capacity is flagged.</summary>
    public const double WindowNearlyFull = 0.8;
    public const int ListMax = 8;
    public const int NamesInDetail = 5;

    public const string Warning = "warning";
    public const string Urgent = "urgent";

    private static bool IsOpen(OrderStatus status) => status is OrderStatus.Pending or OrderStatus.Accepted or OrderStatus.Preparing or OrderStatus.Ready or OrderStatus.OutForDelivery or OrderStatus.Confirmed;

    private static int Minutes(DateTime since, DateTime now) => Math.Max(0, (int)Math.Floor((now - since).TotalMinutes));

    public static TodaySummary Build(
        Guid storeId, string storeName, DateTime nowUtc, IReadOnlyCollection<TodayOrderRow> orders, IReadOnlyCollection<TodayStockRow> stock,
        SlotSettings slots, IReadOnlyDictionary<DateTime, int> booked)
    {
        var dayStart = DateOnly.FromDateTime(nowUtc + SlotRules.India).ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc) - SlotRules.India;
        var today = orders.Where(order => order.CreatedAt >= dayStart && order.Status != OrderStatus.AwaitingPayment).ToArray();
        var open = orders.Where(order => IsOpen(order.Status)).ToArray();
        var delivered = today.Where(order => order.Status is OrderStatus.Delivered or OrderStatus.Completed).ToArray();

        var counts = new TodayCounts(
            today.Length,
            open.Count(order => order.Status == OrderStatus.Pending),
            open.Count(order => order.Status is OrderStatus.Accepted or OrderStatus.Preparing),
            open.Count(order => order.Status == OrderStatus.Ready),
            open.Count(order => order.Status is OrderStatus.OutForDelivery or OrderStatus.Confirmed),
            delivered.Length,
            today.Count(order => order.Status is OrderStatus.Cancelled or OrderStatus.Rejected),
            delivered.Sum(order => order.Total));

        var alerts = new List<TodayAlert>();
        alerts.AddRange(PendingAlerts(open, nowUtc));
        alerts.AddRange(ReadyAlerts(open, nowUtc));
        alerts.AddRange(StockAlerts(stock));
        if (WindowAlert(slots, nowUtc, booked) is { } windows)
        {
            alerts.Add(windows);
        }

        var ordered = alerts.OrderBy(alert => alert.Severity == Urgent ? 0 : 1).ToArray();
        var upcoming = open
            .Where(order => order.SlotStart is not null && order.SlotEnd is not null && order.SlotEnd > nowUtc)
            .OrderBy(order => order.SlotStart).ThenBy(order => order.CreatedAt)
            .Take(ListMax)
            .Select(order => new TodayUpcoming(order.Id, order.OrderNumber, order.SlotStart!.Value, order.SlotEnd!.Value, SlotRules.Label(order.SlotStart.Value, order.SlotEnd.Value, nowUtc), order.Status.ToString()))
            .ToArray();
        return new TodaySummary(storeId, storeName, nowUtc, counts, ordered, upcoming);
    }

    private static IEnumerable<TodayAlert> PendingAlerts(IEnumerable<TodayOrderRow> open, DateTime now)
    {
        foreach (var order in open.Where(item => item.Status == OrderStatus.Pending).OrderBy(item => item.PendingAt).Take(ListMax))
        {
            var minutes = Minutes(order.PendingAt, now);
            if (minutes >= NewOrderWarnMinutes)
            {
                yield return new TodayAlert("PendingTooLong", minutes >= NewOrderUrgentMinutes ? Urgent : Warning, $"{order.OrderNumber} has been waiting {minutes} minutes", "A new order not yet accepted or rejected.", order.Id);
            }
        }
    }

    private static IEnumerable<TodayAlert> ReadyAlerts(IEnumerable<TodayOrderRow> open, DateTime now)
    {
        foreach (var order in open.Where(item => item.Status == OrderStatus.Ready && item.PickedUpAt is null).OrderBy(item => item.ReadyAt ?? item.CreatedAt).Take(ListMax))
        {
            var minutes = Minutes(order.ReadyAt ?? order.CreatedAt, now);
            if (minutes >= ReadyWarnMinutes)
            {
                yield return new TodayAlert("ReadyNotCollected", minutes >= ReadyUrgentMinutes ? Urgent : Warning, $"{order.OrderNumber} has been ready for {minutes} minutes", order.HasPartner ? "Given to a delivery partner who has not picked it up yet." : "No delivery partner has taken it yet.", order.Id);
            }
        }
    }

    private static IEnumerable<TodayAlert> StockAlerts(IReadOnlyCollection<TodayStockRow> stock)
    {
        string Names(IEnumerable<TodayStockRow> rows, bool withCount)
        {
            var list = rows.ToArray();
            var shown = string.Join(", ", list.Take(NamesInDetail).Select(row => withCount ? $"{row.Product} {row.Size} ({row.Available} left)" : $"{row.Product} {row.Size}"));
            return list.Length > NamesInDetail ? $"{shown} and {list.Length - NamesInDetail} more" : shown;
        }

        var none = stock.Where(row => row.Available <= 0).OrderBy(row => row.Product).ToArray();
        if (none.Length > 0)
        {
            yield return new TodayAlert("OutOfStock", Warning, none.Length == 1 ? "1 item is out of stock" : $"{none.Length} items are out of stock", Names(none, false) + ". Customers cannot buy these.");
        }

        var low = stock.Where(row => row.Available > 0 && row.Available <= row.Reorder).OrderBy(row => row.Available).ThenBy(row => row.Product).ToArray();
        if (low.Length > 0)
        {
            yield return new TodayAlert("LowStock", Warning, low.Length == 1 ? "1 item is running low" : $"{low.Length} items are running low", Names(low, true));
        }
    }

    private static TodayAlert? WindowAlert(SlotSettings slots, DateTime now, IReadOnlyDictionary<DateTime, int> booked)
    {
        if (!slots.Enabled)
        {
            return null;
        }

        // Every window of the days on offer counts, including ones inside the cut-off (customers cannot book them, but the store still has to serve them).
        var busy = SlotRules.Windows(slots with { CutoffMinutes = 0 }, now, start => booked.GetValueOrDefault(start))
            .Select(window => (Window: window, Booked: slots.Capacity - window.Left))
            .Where(item => item.Booked >= Math.Ceiling(slots.Capacity * WindowNearlyFull))
            .ToArray();
        if (busy.Length == 0)
        {
            return null;
        }

        var parts = busy.Take(NamesInDetail).Select(item => $"{SlotRules.Label(item.Window.StartUtc, item.Window.EndUtc, now)} ({(item.Window.Available ? $"{item.Booked} of {slots.Capacity}" : "full")})");
        var detail = string.Join(", ", parts) + (busy.Length > NamesInDetail ? $" and {busy.Length - NamesInDetail} more" : string.Empty);
        return new TodayAlert("WindowsNearlyFull", Warning, busy.Length == 1 ? "1 delivery window is nearly full" : $"{busy.Length} delivery windows are nearly full", detail);
    }
}
