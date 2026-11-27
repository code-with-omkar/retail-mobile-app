using QuickCommerce.Application.Services;
using QuickCommerce.Domain;
using Xunit;

namespace QuickCommerce.Tests;

/// <summary>Anonymous analytics: what an event may contain and how the funnel is worked out, on their own (no database).</summary>
public sealed class AnalyticsRulesTests
{
    [Fact]
    public void Only_the_known_steps_are_accepted()
    {
        foreach (var name in AnalyticsNames.All)
        {
            Assert.True(AnalyticsRules.IsKnown(name), name);
        }

        Assert.False(AnalyticsRules.IsKnown("hack"));
        Assert.False(AnalyticsRules.IsKnown("APP_OPEN"));
        Assert.False(AnalyticsRules.IsKnown(null));
        Assert.False(AnalyticsRules.IsKnown(""));
    }

    [Theory]
    [InlineData("3f2b8c1e9a7d4e56", true)]
    [InlineData("3f2b8c1e-9a7d-4e56-8b11-2c3d4e5f6a7b", true)]
    [InlineData("short", false)]
    [InlineData("has spaces in it 1234567890", false)]
    [InlineData("emoji-😀-0123456789012", false)]
    [InlineData("", false)]
    public void A_session_id_is_16_to_64_letters_digits_dashes_or_underscores(string id, bool ok) => Assert.Equal(ok, AnalyticsRules.IsSession(id));

    [Fact]
    public void A_session_id_over_64_characters_or_missing_is_refused()
    {
        Assert.False(AnalyticsRules.IsSession(new string('a', 65)));
        Assert.True(AnalyticsRules.IsSession(new string('a', 64)));
        Assert.False(AnalyticsRules.IsSession(null));
    }

    [Theory]
    [InlineData("Tomato", "tomato")]
    [InlineData("  Fresh   MILK  ", "fresh milk")]
    [InlineData("atta 5kg!", "atta 5kg")]
    [InlineData("rice+dal", "rice+dal")]
    [InlineData("cash+coupon+slot", "cash+coupon+slot")]
    [InlineData("BelowMinimum", "belowminimum")]
    public void A_detail_is_made_lower_case_with_only_safe_characters(string typed, string expected) => Assert.Equal(expected, AnalyticsRules.CleanDetail(typed));

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("!!!")]
    [InlineData("call me on 9876543210")]
    [InlineData("98765 43210 asha")]
    [InlineData("asha@example.com")]
    [InlineData("1234567")]
    public void Anything_that_looks_personal_or_is_empty_is_dropped_altogether(string? typed) => Assert.Null(AnalyticsRules.CleanDetail(typed));

    [Fact]
    public void A_few_digits_in_a_search_are_fine_but_seven_or_more_are_not_however_they_are_spaced()
    {
        Assert.Equal("atta 5", AnalyticsRules.CleanDetail("atta 5"));
        Assert.Equal("size 123456", AnalyticsRules.CleanDetail("size 123456"));
        Assert.Equal("atta 5 kg 10 pack", AnalyticsRules.CleanDetail("Atta 5 kg 10 pack")); // 3 digits
        Assert.Null(AnalyticsRules.CleanDetail("size 1234567"));
        Assert.Null(AnalyticsRules.CleanDetail("98765-43210"));
        Assert.Null(AnalyticsRules.CleanDetail("9 8 7 6 5 4 3"));
    }

    [Fact]
    public void A_long_detail_is_cut_at_40_characters_without_a_trailing_space()
    {
        var cleaned = AnalyticsRules.CleanDetail(new string('a', 35) + " " + new string('b', 30))!;
        Assert.True(cleaned.Length <= AnalyticsRules.DetailMax);
        Assert.False(cleaned.EndsWith(' '));
    }

    [Fact]
    public void The_funnel_gives_each_steps_share_of_the_first_and_of_the_step_before()
    {
        var steps = AnalyticsRules.Funnel(new Dictionary<string, int>
        {
            [AnalyticsNames.AppOpen] = 200, [AnalyticsNames.ProductView] = 100, [AnalyticsNames.AddToCart] = 40, [AnalyticsNames.CheckoutStart] = 20, [AnalyticsNames.OrderPlaced] = 10,
        });

        Assert.Equal(["app_open", "product_view", "add_to_cart", "checkout_start", "order_placed"], steps.Select(step => step.Name));
        Assert.Equal([100, 50, 20, 10, 5], steps.Select(step => step.PercentOfFirst));
        Assert.Equal([100, 50, 40, 50, 50], steps.Select(step => step.PercentOfPrevious));
    }

    [Fact]
    public void An_empty_funnel_is_all_zeros_without_dividing_by_zero()
    {
        var steps = AnalyticsRules.Funnel(new Dictionary<string, int>());
        Assert.All(steps, step => { Assert.Equal(0, step.Sessions); Assert.Equal(0, step.PercentOfFirst); Assert.Equal(0, step.PercentOfPrevious); });
        Assert.Equal(5, steps.Count);
    }

    [Fact]
    public void A_step_with_more_sessions_than_the_one_before_is_capped_at_100_percent()
    {
        var steps = AnalyticsRules.Funnel(new Dictionary<string, int> { [AnalyticsNames.AppOpen] = 10, [AnalyticsNames.ProductView] = 4, [AnalyticsNames.AddToCart] = 9 });
        Assert.Equal(90, steps[2].PercentOfFirst);
        Assert.Equal(100, steps[2].PercentOfPrevious); // 9 of 4 is more than all of them
    }

    [Fact]
    public void A_missing_first_step_gives_zero_percent_of_first_for_every_step()
    {
        var steps = AnalyticsRules.Funnel(new Dictionary<string, int> { [AnalyticsNames.AddToCart] = 7 });
        Assert.All(steps, step => Assert.Equal(0, step.PercentOfFirst));
    }

    [Theory]
    [InlineData(1, 3, 33)]
    [InlineData(2, 3, 67)]
    [InlineData(1, 2, 50)]
    [InlineData(0, 5, 0)]
    [InlineData(5, 0, 0)]
    [InlineData(9, 3, 100)]
    public void Percent_is_a_rounded_whole_number_never_above_100(int part, int whole, int expected) => Assert.Equal(expected, AnalyticsRules.Percent(part, whole));

    [Fact]
    public void An_event_belongs_to_the_india_day_not_the_utc_day()
    {
        Assert.Equal(new DateOnly(2026, 11, 2), AnalyticsRules.DayOf(new DateTime(2026, 11, 1, 18, 30, 0, DateTimeKind.Utc))); // 00:00 on the 2nd in India
        Assert.Equal(new DateOnly(2026, 11, 1), AnalyticsRules.DayOf(new DateTime(2026, 11, 1, 18, 29, 59, DateTimeKind.Utc)));
    }

    [Fact]
    public void The_events_table_holds_no_user_account_phone_email_or_address_field()
    {
        var names = typeof(AnalyticsEvent).GetProperties().Select(property => property.Name).Concat(typeof(AnalyticsDaily).GetProperties().Select(property => property.Name)).ToArray();
        foreach (var forbidden in new[] { "UserId", "CustomerId", "Phone", "Email", "Address", "Ip", "Latitude", "Longitude", "Token" })
        {
            Assert.DoesNotContain(names, name => name.Contains(forbidden, StringComparison.OrdinalIgnoreCase));
        }
    }
}
