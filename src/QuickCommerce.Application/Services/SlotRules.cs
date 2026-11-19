using QuickCommerce.Domain;

namespace QuickCommerce.Application.Services;

/// <summary>What a store's slot settings are, as the rules use them.</summary>
public sealed record SlotSettings(bool Enabled, int OpenMinutes, int CloseMinutes, int WindowMinutes, int Capacity, int CutoffMinutes, int DaysAhead, bool AsapAllowed)
{
    /// <summary>Nothing scheduled: every order is as soon as possible.</summary>
    public static SlotSettings Off { get; } = new(false, 9 * 60, 21 * 60, 60, 10, 60, 2, true);

    public static SlotSettings From(DeliverySlotSetting row) => new(row.Enabled, row.OpenMinutes, row.CloseMinutes, row.WindowMinutes, row.Capacity, row.CutoffMinutes, row.DaysAhead, row.AsapAllowed);
}

/// <summary>One window a customer can choose. <see cref="Left"/> is how many more orders it takes.</summary>
public sealed record SlotWindow(DateTime StartUtc, DateTime EndUtc, int Left)
{
    public bool Available => Left > 0;
}

/// <summary>
/// The rules for scheduled delivery, written once. The slot list shown to the customer and the check at checkout both come from here, so a
/// window offered is a window accepted. Times are India time (no daylight saving), kept in UTC on orders.
/// </summary>
public static class SlotRules
{
    public static readonly TimeSpan India = TimeSpan.FromHours(5.5);
    public static readonly int[] AllowedWindowMinutes = [30, 45, 60, 90, 120, 180];

    public static string? Validate(SlotSettings s)
    {
        if (s.OpenMinutes is < 0 or > 1435 || s.CloseMinutes is < 5 or > 1440 || s.OpenMinutes % 5 != 0 || s.CloseMinutes % 5 != 0)
        {
            return "Opening and closing times must be within the day, in steps of 5 minutes.";
        }

        if (s.CloseMinutes <= s.OpenMinutes)
        {
            return "The closing time must be after the opening time.";
        }

        if (!AllowedWindowMinutes.Contains(s.WindowMinutes))
        {
            return "A window can be 30, 45, 60, 90, 120 or 180 minutes long.";
        }

        if (s.CloseMinutes - s.OpenMinutes < s.WindowMinutes)
        {
            return "The opening hours are shorter than one window.";
        }

        if (s.Capacity is < 1 or > 500)
        {
            return "A window can take 1 to 500 orders.";
        }

        if (s.CutoffMinutes is < 0 or > 1440)
        {
            return "The cut-off is 0 to 1440 minutes.";
        }

        if (s.DaysAhead is < 1 or > 7)
        {
            return "Customers can choose 1 to 7 days, counting today.";
        }

        return s.Enabled || s.AsapAllowed ? null : "Switch on 'as soon as possible' or on scheduled delivery; with both off nobody could order.";
    }

    /// <summary>
    /// Every window a customer could pick now, oldest first: today and the days ahead, inside opening hours, starting at least the cut-off from
    /// now. <paramref name="booked"/> says how many orders each window (by start, UTC) already holds. Empty when scheduling is off.
    /// </summary>
    public static IReadOnlyList<SlotWindow> Windows(SlotSettings s, DateTime nowUtc, Func<DateTime, int> booked)
    {
        if (!s.Enabled)
        {
            return [];
        }

        var earliest = nowUtc.AddMinutes(s.CutoffMinutes);
        var today = DateOnly.FromDateTime(nowUtc + India);
        var windows = new List<SlotWindow>();
        for (var day = 0; day < s.DaysAhead; day++)
        {
            var midnightUtc = today.AddDays(day).ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc) - India;
            for (var start = s.OpenMinutes; start + s.WindowMinutes <= s.CloseMinutes; start += s.WindowMinutes)
            {
                var startUtc = midnightUtc.AddMinutes(start);
                if (startUtc < earliest)
                {
                    continue;
                }

                windows.Add(new SlotWindow(startUtc, startUtc.AddMinutes(s.WindowMinutes), Math.Max(0, s.Capacity - booked(startUtc))));
            }
        }

        return windows;
    }

    public enum Check { Ok, NotOffered, UnknownWindow, Full, AsapNotAllowed }

    /// <summary>
    /// What a customer's choice means. A null <paramref name="chosenStartUtc"/> is "as soon as possible", allowed unless the store turned it
    /// off. A start must be exactly one of the windows currently on offer, and not full.
    /// </summary>
    public static (Check Result, SlotWindow? Window, string Message) Pick(SlotSettings s, DateTime nowUtc, DateTime? chosenStartUtc, Func<DateTime, int> booked)
    {
        if (chosenStartUtc is null)
        {
            return s.Enabled && !s.AsapAllowed
                ? (Check.AsapNotAllowed, null, "Choose a delivery time.")
                : (Check.Ok, null, string.Empty);
        }

        if (!s.Enabled)
        {
            return (Check.NotOffered, null, "This store does not take scheduled deliveries.");
        }

        var wanted = DateTime.SpecifyKind(chosenStartUtc.Value, DateTimeKind.Utc);
        var window = Windows(s, nowUtc, booked).FirstOrDefault(item => item.StartUtc == wanted);
        if (window is null)
        {
            return (Check.UnknownWindow, null, "That delivery time is no longer available. Choose another.");
        }

        return window.Available ? (Check.Ok, window, string.Empty) : (Check.Full, window, "That delivery time is full. Choose another.");
    }

    /// <summary>"Today 2 pm - 3 pm", "Tomorrow 9 am - 10 am", "Wed 4 Nov, 9 am - 10 am".</summary>
    public static string Label(DateTime startUtc, DateTime endUtc, DateTime nowUtc)
    {
        var start = startUtc + India;
        var end = endUtc + India;
        var days = start.Date.Subtract((nowUtc + India).Date).Days;
        var day = days == 0 ? "Today" : days == 1 ? "Tomorrow" : start.ToString("ddd d MMM", System.Globalization.CultureInfo.InvariantCulture);
        string Clock(DateTime time) => time.ToString("h:mm tt", System.Globalization.CultureInfo.InvariantCulture).Replace(":00", string.Empty).ToLowerInvariant();
        return $"{day}, {Clock(start)} - {Clock(end)}";
    }
}
