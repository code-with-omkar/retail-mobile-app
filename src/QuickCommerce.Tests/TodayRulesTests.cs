using QuickCommerce.Application.Interfaces;
using QuickCommerce.Application.Services;
using QuickCommerce.Domain;
using Xunit;

namespace QuickCommerce.Tests;

/// <summary>The Today screen of a store: counts and the alert limits, on their own (no database).</summary>
public sealed class TodayRulesTests
{
    // 10:00 in India on Mon 2 Nov 2026 is 04:30 UTC.
    private static readonly DateTime Now = new(2026, 11, 2, 4, 30, 0, DateTimeKind.Utc);
    private static readonly Guid Store = Guid.NewGuid();

    private static DateTime India(int day, int hour, int minute = 0) => new DateTime(2026, 11, day, hour, minute, 0, DateTimeKind.Utc) - SlotRules.India;

    private static TodayOrderRow Order(string number, OrderStatus status, int minutesAgo = 1, int? readyMinutesAgo = null, bool partner = false, DateTime? picked = null, decimal total = 100, DateTime? slotStart = null, int? placedMinutesAgo = null)
    {
        var placed = Now.AddMinutes(-(placedMinutesAgo ?? minutesAgo));
        return new TodayOrderRow(Guid.NewGuid(), number, status, placed, Now.AddMinutes(-minutesAgo), readyMinutesAgo is { } r ? Now.AddMinutes(-r) : null, partner, picked, total, slotStart, slotStart?.AddMinutes(60));
    }

    private static TodaySummary Build(IEnumerable<TodayOrderRow>? orders = null, IEnumerable<TodayStockRow>? stock = null, SlotSettings? slots = null, IReadOnlyDictionary<DateTime, int>? booked = null) =>
        TodayRules.Build(Store, "Kharghar", Now, (orders ?? []).ToArray(), (stock ?? []).ToArray(), slots ?? SlotSettings.Off, booked ?? new Dictionary<DateTime, int>());

    [Fact]
    public void An_empty_day_has_zero_counts_and_nothing_to_flag()
    {
        var today = Build();
        Assert.Equal(new TodayCounts(0, 0, 0, 0, 0, 0, 0, 0), today.Counts);
        Assert.Empty(today.Alerts);
        Assert.Empty(today.Upcoming);
        Assert.Equal("Kharghar", today.StoreName);
    }

    [Fact]
    public void Counts_group_the_statuses_and_revenue_is_from_delivered_orders_only()
    {
        var today = Build([
            Order("A", OrderStatus.Pending), Order("B", OrderStatus.Accepted), Order("C", OrderStatus.Preparing), Order("D", OrderStatus.Ready),
            Order("E", OrderStatus.OutForDelivery), Order("F", OrderStatus.Delivered, total: 250), Order("G", OrderStatus.Completed, total: 50),
            Order("H", OrderStatus.Cancelled, total: 999), Order("I", OrderStatus.Rejected, total: 999),
        ]);

        Assert.Equal(9, today.Counts.Placed);
        Assert.Equal(1, today.Counts.New);
        Assert.Equal(2, today.Counts.Packing);
        Assert.Equal(1, today.Counts.Ready);
        Assert.Equal(1, today.Counts.OutForDelivery);
        Assert.Equal(2, today.Counts.Delivered);
        Assert.Equal(2, today.Counts.Cancelled);
        Assert.Equal(300m, today.Counts.Revenue);
    }

    [Fact]
    public void Open_orders_from_yesterday_still_count_as_open_but_not_as_placed_today()
    {
        var yesterday = Order("OLD", OrderStatus.Accepted, minutesAgo: 60, placedMinutesAgo: 60 * 24);
        var earlierDelivered = Order("DONE", OrderStatus.Delivered, placedMinutesAgo: 60 * 24, total: 500);

        var today = Build([yesterday, earlierDelivered]);

        Assert.Equal(0, today.Counts.Placed);
        Assert.Equal(1, today.Counts.Packing); // still being packed, whatever day it was placed
        Assert.Equal(0, today.Counts.Delivered);
        Assert.Equal(0m, today.Counts.Revenue);
    }

    [Fact]
    public void The_day_starts_at_midnight_india_time_not_utc()
    {
        // 00:05 India on the 2nd is 18:35 UTC on the 1st: placed "today" in India even though it is yesterday in UTC.
        var justAfterMidnight = Order("EARLY", OrderStatus.Delivered, total: 10, placedMinutesAgo: 10 * 60 - 5);
        var beforeMidnight = Order("LATE", OrderStatus.Delivered, total: 20, placedMinutesAgo: 10 * 60 + 5);

        var today = Build([justAfterMidnight, beforeMidnight]);

        Assert.Equal(1, today.Counts.Placed);
        Assert.Equal(10m, today.Counts.Revenue);
    }

    [Theory]
    [InlineData(4, false, null)]
    [InlineData(5, true, "warning")]
    [InlineData(9, true, "warning")]
    [InlineData(10, true, "urgent")]
    [InlineData(45, true, "urgent")]
    public void A_new_order_is_flagged_after_5_minutes_and_urgent_after_10(int minutes, bool flagged, string? severity)
    {
        var order = Order("N-1", OrderStatus.Pending, minutesAgo: minutes);

        var alert = Build([order]).Alerts.SingleOrDefault(item => item.Kind == "PendingTooLong");

        Assert.Equal(flagged, alert is not null);
        if (alert is not null)
        {
            Assert.Equal(severity, alert.Severity);
            Assert.Equal(order.Id, alert.OrderId);
            Assert.Contains($"{minutes} minutes", alert.Title);
        }
    }

    [Fact]
    public void An_accepted_order_is_not_a_waiting_new_order_however_old()
    {
        Assert.DoesNotContain(Build([Order("A", OrderStatus.Accepted, minutesAgo: 90)]).Alerts, alert => alert.Kind == "PendingTooLong");
    }

    [Theory]
    [InlineData(19, false, null)]
    [InlineData(20, true, "warning")]
    [InlineData(39, true, "warning")]
    [InlineData(40, true, "urgent")]
    public void A_ready_order_is_flagged_after_20_minutes_and_urgent_after_40(int minutes, bool flagged, string? severity)
    {
        var alert = Build([Order("R-1", OrderStatus.Ready, readyMinutesAgo: minutes)]).Alerts.SingleOrDefault(item => item.Kind == "ReadyNotCollected");

        Assert.Equal(flagged, alert is not null);
        Assert.Equal(severity, alert?.Severity);
    }

    [Fact]
    public void A_ready_order_says_whether_a_partner_has_it_and_one_already_picked_up_is_not_flagged()
    {
        var without = Build([Order("R-1", OrderStatus.Ready, readyMinutesAgo: 25)]).Alerts.Single();
        var with = Build([Order("R-2", OrderStatus.Ready, readyMinutesAgo: 25, partner: true)]).Alerts.Single();
        var picked = Build([Order("R-3", OrderStatus.Ready, readyMinutesAgo: 25, partner: true, picked: Now.AddMinutes(-3))]);

        Assert.Contains("No delivery partner", without.Detail);
        Assert.Contains("has not picked it up", with.Detail);
        Assert.Empty(picked.Alerts);
    }

    [Fact]
    public void Out_of_stock_and_low_stock_are_two_alerts_naming_the_items_with_the_count_left()
    {
        var alerts = Build(stock: [
            new TodayStockRow("Milk", "1 l", 0, 5), new TodayStockRow("Bread", "400 g", 2, 5), new TodayStockRow("Eggs", "12", 5, 5), new TodayStockRow("Rice", "5 kg", 9, 5),
        ]).Alerts;

        var none = alerts.Single(alert => alert.Kind == "OutOfStock");
        var low = alerts.Single(alert => alert.Kind == "LowStock");
        Assert.Equal("1 item is out of stock", none.Title);
        Assert.Contains("Milk 1 l", none.Detail);
        Assert.Equal("2 items are running low", low.Title);
        Assert.Contains("Bread 400 g (2 left)", low.Detail);
        Assert.Contains("Eggs 12 (5 left)", low.Detail); // at the reorder level counts
        Assert.DoesNotContain("Rice", low.Detail);        // above it does not
    }

    [Fact]
    public void A_long_list_of_items_is_cut_after_five_with_a_count_of_the_rest()
    {
        var stock = Enumerable.Range(1, 8).Select(i => new TodayStockRow($"Item{i}", "1", 0, 5)).ToArray();
        var alert = Build(stock: stock).Alerts.Single();
        Assert.Equal("8 items are out of stock", alert.Title);
        Assert.Contains("and 3 more", alert.Detail);
    }

    [Fact]
    public void Windows_at_80_percent_or_more_are_flagged_and_a_full_one_says_full()
    {
        var slots = new SlotSettings(true, 0, 1440, 60, 5, 0, 2, true);
        var booked = new Dictionary<DateTime, int> { [India(2, 14)] = 4, [India(2, 15)] = 5, [India(2, 16)] = 3 };

        var alert = Build(slots: slots, booked: booked).Alerts.Single(item => item.Kind == "WindowsNearlyFull");

        Assert.Equal("2 delivery windows are nearly full", alert.Title);
        Assert.Contains("Today, 2 pm - 3 pm (4 of 5)", alert.Detail);
        Assert.Contains("Today, 3 pm - 4 pm (full)", alert.Detail);
        Assert.DoesNotContain("4 pm - 5 pm", alert.Detail); // 3 of 5 is 60 percent
    }

    [Fact]
    public void Windows_are_not_flagged_when_the_store_does_not_schedule_or_nothing_is_busy()
    {
        Assert.DoesNotContain(Build(slots: SlotSettings.Off, booked: new Dictionary<DateTime, int> { [India(2, 14)] = 9 }).Alerts, a => a.Kind == "WindowsNearlyFull");
        Assert.DoesNotContain(Build(slots: new SlotSettings(true, 0, 1440, 60, 5, 0, 2, true)).Alerts, a => a.Kind == "WindowsNearlyFull");
    }

    [Fact]
    public void Urgent_alerts_come_first_and_the_rest_keep_their_order()
    {
        var alerts = Build(
            [Order("P-1", OrderStatus.Pending, minutesAgo: 6), Order("P-2", OrderStatus.Pending, minutesAgo: 12)],
            [new TodayStockRow("Milk", "1 l", 0, 5)]).Alerts;

        Assert.Equal("urgent", alerts[0].Severity);
        Assert.Equal(["PendingTooLong", "PendingTooLong", "OutOfStock"], alerts.Select(a => a.Kind));
        Assert.Contains("P-2", alerts[0].Title);
    }

    [Fact]
    public void Upcoming_lists_open_scheduled_orders_soonest_window_first_and_skips_finished_ones()
    {
        var later = Order("LATE", OrderStatus.Accepted, slotStart: India(3, 9));
        var sooner = Order("SOON", OrderStatus.Pending, slotStart: India(2, 14));
        var done = Order("DONE", OrderStatus.Delivered, slotStart: India(2, 11));
        var asap = Order("ASAP", OrderStatus.Pending);
        var over = Order("OVER", OrderStatus.Accepted, slotStart: India(2, 8)); // its window ended at 9, before now (10:00)

        var upcoming = Build([later, sooner, done, asap, over]).Upcoming;

        Assert.Equal(["SOON", "LATE"], upcoming.Select(item => item.OrderNumber));
        Assert.Equal("Today, 2 pm - 3 pm", upcoming[0].Label);
        Assert.Equal("Tomorrow, 9 am - 10 am", upcoming[1].Label);
        Assert.Equal("Pending", upcoming[0].Status);
    }

    [Fact]
    public void Lists_are_capped()
    {
        var orders = Enumerable.Range(0, 20).Select(i => Order($"P-{i}", OrderStatus.Pending, minutesAgo: 30 + i)).ToArray();
        var scheduled = Enumerable.Range(0, 20).Select(i => Order($"S-{i}", OrderStatus.Accepted, slotStart: India(3, 9).AddMinutes(i))).ToArray();

        Assert.Equal(TodayRules.ListMax, Build(orders).Alerts.Count);
        Assert.Equal(TodayRules.ListMax, Build(scheduled).Upcoming.Count);
    }

    [Fact]
    public void An_order_awaiting_payment_is_nobodys_business_yet()
    {
        var today = Build([Order("W", OrderStatus.AwaitingPayment, minutesAgo: 30)]);
        Assert.Equal(0, today.Counts.Placed);
        Assert.Empty(today.Alerts);
    }
}
