using QuickCommerce.Application.Services;
using Xunit;

namespace QuickCommerce.Tests;

/// <summary>Delivery slots, step 1: the rules on their own (no database).</summary>
public sealed class SlotRulesTests
{
    // 10:00 India time on Mon 2 Nov 2026 is 04:30 UTC.
    private static readonly DateTime Now = new(2026, 11, 2, 4, 30, 0, DateTimeKind.Utc);

    private static SlotSettings On(int window = 60, int capacity = 3, int cutoff = 60, int days = 2, bool asap = true, int open = 9 * 60, int close = 21 * 60) =>
        new(true, open, close, window, capacity, cutoff, days, asap);

    private static DateTime India(int day, int hour, int minute = 0) => new DateTime(2026, 11, day, hour, minute, 0, DateTimeKind.Utc) - SlotRules.India;

    private static int None(DateTime _) => 0;

    [Fact]
    public void Scheduling_off_offers_no_windows_and_asap_is_the_only_choice()
    {
        Assert.Empty(SlotRules.Windows(SlotSettings.Off, Now, None));
        var (check, window, _) = SlotRules.Pick(SlotSettings.Off, Now, null, None);
        Assert.Equal(SlotRules.Check.Ok, check);
        Assert.Null(window);
    }

    [Fact]
    public void Windows_cover_the_opening_hours_today_and_the_days_ahead_after_the_cutoff()
    {
        var windows = SlotRules.Windows(On(), Now, None);

        // Now is 10:00, cut-off 60 minutes: today's first window is 11:00-12:00, the last 20:00-21:00 (10 windows), then 12 on the next day.
        Assert.Equal(India(2, 11), windows[0].StartUtc);
        Assert.Equal(India(2, 12), windows[0].EndUtc);
        Assert.Equal(India(2, 20), windows[9].StartUtc);
        Assert.Equal(India(3, 9), windows[10].StartUtc);
        Assert.Equal(22, windows.Count);
        Assert.Equal(India(3, 20), windows[^1].StartUtc);
    }

    [Fact]
    public void A_window_that_starts_exactly_at_the_cutoff_is_still_open_and_one_a_minute_earlier_is_not()
    {
        Assert.Equal(India(2, 11), SlotRules.Windows(On(), Now, None)[0].StartUtc); // 10:00 + 60 = 11:00
        Assert.Equal(India(2, 12), SlotRules.Windows(On(), Now.AddMinutes(1), None)[0].StartUtc); // 10:01 + 60 > 11:00
    }

    [Fact]
    public void A_last_partial_window_is_dropped_and_other_lengths_fit()
    {
        var windows = SlotRules.Windows(On(window: 90, close: 9 * 60 + 300, days: 1, cutoff: 0), Now.AddHours(-2), None); // 08:00 now, open 9-14
        Assert.Equal([India(2, 9), India(2, 10, 30), India(2, 12)], windows.Select(w => w.StartUtc));
        Assert.Equal(India(2, 13, 30), windows[^1].EndUtc);
    }

    [Fact]
    public void Windows_show_how_many_places_are_left_and_a_full_one_is_not_available()
    {
        var full = India(2, 12);
        var windows = SlotRules.Windows(On(capacity: 3), Now, start => start == full ? 3 : start == India(2, 13) ? 1 : 0);

        Assert.Equal(3, windows.Single(w => w.StartUtc == India(2, 11)).Left);
        Assert.False(windows.Single(w => w.StartUtc == full).Available);
        Assert.Equal(2, windows.Single(w => w.StartUtc == India(2, 13)).Left);
    }

    [Fact]
    public void An_overbooked_window_never_shows_a_negative_number()
    {
        Assert.Equal(0, SlotRules.Windows(On(capacity: 2), Now, _ => 5)[0].Left);
    }

    [Fact]
    public void After_midnight_india_time_the_new_day_is_today()
    {
        var justAfterMidnight = India(3, 0, 5); // 00:05 on the 3rd, India time
        var windows = SlotRules.Windows(On(days: 1, cutoff: 0), justAfterMidnight, None);
        Assert.Equal(India(3, 9), windows[0].StartUtc);
    }

    [Fact]
    public void A_chosen_window_must_be_one_on_offer_and_not_full()
    {
        var s = On();
        Assert.Equal(SlotRules.Check.Ok, SlotRules.Pick(s, Now, India(2, 14), None).Result);
        Assert.Equal(SlotRules.Check.UnknownWindow, SlotRules.Pick(s, Now, India(2, 14, 30), None).Result); // not on the grid
        Assert.Equal(SlotRules.Check.UnknownWindow, SlotRules.Pick(s, Now, India(2, 10), None).Result);     // inside the cut-off
        Assert.Equal(SlotRules.Check.UnknownWindow, SlotRules.Pick(s, Now, India(5, 9), None).Result);      // beyond the days ahead
        Assert.Equal(SlotRules.Check.Full, SlotRules.Pick(s, Now, India(2, 14), start => start == India(2, 14) ? 3 : 0).Result);
    }

    [Fact]
    public void A_start_given_with_another_kind_of_time_still_matches_the_same_moment()
    {
        var unspecified = DateTime.SpecifyKind(India(2, 14), DateTimeKind.Unspecified);
        Assert.Equal(SlotRules.Check.Ok, SlotRules.Pick(On(), Now, unspecified, None).Result);
    }

    [Fact]
    public void A_window_cannot_be_chosen_when_the_store_does_not_schedule()
    {
        Assert.Equal(SlotRules.Check.NotOffered, SlotRules.Pick(SlotSettings.Off, Now, India(2, 14), None).Result);
    }

    [Fact]
    public void Asap_can_be_switched_off_so_a_time_must_be_chosen()
    {
        var s = On(asap: false);
        Assert.Equal(SlotRules.Check.AsapNotAllowed, SlotRules.Pick(s, Now, null, None).Result);
        Assert.Equal(SlotRules.Check.Ok, SlotRules.Pick(s, Now, India(2, 14), None).Result);
    }

    [Theory]
    [InlineData(true, 540, 1260, 60, 10, 60, 2, true, true)]
    [InlineData(true, 540, 1260, 50, 10, 60, 2, true, false)]  // window length not allowed
    [InlineData(true, 1260, 540, 60, 10, 60, 2, true, false)]  // closes before it opens
    [InlineData(true, 540, 570, 60, 10, 60, 2, true, false)]   // hours shorter than a window
    [InlineData(true, 541, 1260, 60, 10, 60, 2, true, false)]  // not in steps of 5
    [InlineData(true, 540, 1260, 60, 0, 60, 2, true, false)]   // no capacity
    [InlineData(true, 540, 1260, 60, 501, 60, 2, true, false)]
    [InlineData(true, 540, 1260, 60, 10, -1, 2, true, false)]
    [InlineData(true, 540, 1260, 60, 10, 60, 0, true, false)]
    [InlineData(true, 540, 1260, 60, 10, 60, 8, true, false)]
    [InlineData(true, 540, 1260, 60, 10, 60, 2, false, true)]  // scheduled only is fine
    [InlineData(false, 540, 1260, 60, 10, 60, 2, false, false)] // both off: nobody could order
    [InlineData(true, 0, 1440, 180, 1, 0, 7, true, true)]
    public void Settings_are_checked_before_they_are_saved(bool enabled, int open, int close, int window, int capacity, int cutoff, int days, bool asap, bool ok) =>
        Assert.Equal(ok, SlotRules.Validate(new SlotSettings(enabled, open, close, window, capacity, cutoff, days, asap)) is null);

    [Fact]
    public void Labels_say_today_tomorrow_or_the_date_in_india_time()
    {
        Assert.Equal("Today, 2 pm - 3 pm", SlotRules.Label(India(2, 14), India(2, 15), Now));
        Assert.Equal("Tomorrow, 9 am - 10:30 am", SlotRules.Label(India(3, 9), India(3, 10, 30), Now));
        Assert.Equal("Wed 4 Nov, 12 pm - 1 pm", SlotRules.Label(India(4, 12), India(4, 13), Now));
    }
}
