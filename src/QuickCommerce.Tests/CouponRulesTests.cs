using QuickCommerce.Application.Services;
using QuickCommerce.Domain;
using Xunit;

namespace QuickCommerce.Tests;

/// <summary>Coupons, step 1: the rules on their own (no database).</summary>
public sealed class CouponRulesTests
{
    private static readonly DateTime Now = new(2026, 11, 1, 10, 0, 0, DateTimeKind.Utc);

    private static Coupon Make(string kind = CouponKinds.Percent, decimal value = 10, decimal? cap = null, decimal min = 0) => new()
    {
        Code = "SAVE10", Kind = kind, Value = value, MaxDiscount = cap, MinSubtotal = min, StartsAt = Now.AddDays(-1),
    };

    private static CouponContext Ctx(decimal subtotal = 500, decimal delivery = 30, int all = 0, int mine = 0, bool hasOrders = true) => new(subtotal, delivery, Now, all, mine, hasOrders);

    [Theory]
    [InlineData(" save-10 ", "SAVE-10")]
    [InlineData("Sa ve\t10", "SAVE10")]
    [InlineData(null, "")]
    public void The_code_is_stored_without_spaces_and_in_upper_case(string? typed, string expected) => Assert.Equal(expected, CouponRules.Normalize(typed));

    [Theory]
    [InlineData("SAVE10", true)]
    [InlineData("A-B_9", true)]
    [InlineData("AB", false)]
    [InlineData("WAYTOOLONGCODE1234567", false)]
    [InlineData("BAD CODE", false)]
    [InlineData("save10", false)]
    [InlineData("OFFER%", false)]
    public void A_code_is_3_to_20_letters_digits_dashes_or_underscores(string code, bool ok) => Assert.Equal(ok, CouponRules.IsValidCode(code));

    [Fact]
    public void Percent_is_worked_out_on_the_product_total_and_rounded_to_paise()
    {
        Assert.Equal(50m, CouponRules.Discount(CouponKinds.Percent, 10, null, 500, 30));
        Assert.Equal(3.34m, CouponRules.Discount(CouponKinds.Percent, 10, null, 33.35m, 0)); // 3.335 rounds up
    }

    [Fact]
    public void Percent_stops_at_its_cap()
    {
        Assert.Equal(100m, CouponRules.Discount(CouponKinds.Percent, 50, 100, 1000, 0));
        Assert.Equal(50m, CouponRules.Discount(CouponKinds.Percent, 50, 100, 100, 0));
    }

    [Fact]
    public void Flat_never_takes_more_than_the_product_total()
    {
        Assert.Equal(50m, CouponRules.Discount(CouponKinds.Flat, 50, null, 500, 30));
        Assert.Equal(40m, CouponRules.Discount(CouponKinds.Flat, 50, null, 40, 30));
    }

    [Fact]
    public void Free_delivery_is_the_delivery_fee_and_nothing_when_there_is_none()
    {
        Assert.Equal(30m, CouponRules.Discount(CouponKinds.FreeDelivery, 0, null, 500, 30));
        Assert.Equal(0m, CouponRules.Discount(CouponKinds.FreeDelivery, 0, null, 500, 0));
    }

    [Fact]
    public void An_empty_cart_or_an_unknown_kind_gives_no_discount()
    {
        Assert.Equal(0m, CouponRules.Discount(CouponKinds.Percent, 10, null, 0, 30));
        Assert.Equal(0m, CouponRules.Discount("Mystery", 10, null, 500, 30));
    }

    [Fact]
    public void A_good_code_is_valid_and_carries_the_discount()
    {
        var outcome = CouponRules.Check(Make(), Ctx());
        Assert.Equal(CouponCheck.Valid, outcome.Check);
        Assert.Equal(50m, outcome.Discount);
    }

    [Fact]
    public void An_unknown_or_switched_off_code_is_refused()
    {
        Assert.Equal(CouponCheck.NotFound, CouponRules.Check(null, Ctx()).Check);
        var off = Make();
        off.IsActive = false;
        Assert.Equal(CouponCheck.Inactive, CouponRules.Check(off, Ctx()).Check);
    }

    [Fact]
    public void The_start_is_inclusive_and_the_end_is_not()
    {
        var coupon = Make();
        coupon.StartsAt = Now;
        coupon.EndsAt = Now.AddHours(1);
        Assert.Equal(CouponCheck.Valid, CouponRules.Check(coupon, Ctx()).Check);

        coupon.StartsAt = Now.AddSeconds(1);
        Assert.Equal(CouponCheck.NotStarted, CouponRules.Check(coupon, Ctx()).Check);

        coupon.StartsAt = Now.AddDays(-1);
        coupon.EndsAt = Now;
        Assert.Equal(CouponCheck.Expired, CouponRules.Check(coupon, Ctx()).Check);
    }

    [Fact]
    public void The_minimum_order_is_checked_and_the_message_says_how_much_more()
    {
        var coupon = Make(min: 600);
        var outcome = CouponRules.Check(coupon, Ctx(subtotal: 500));
        Assert.Equal(CouponCheck.BelowMinimum, outcome.Check);
        Assert.Contains("100", outcome.Message);
        Assert.Equal(CouponCheck.Valid, CouponRules.Check(coupon, Ctx(subtotal: 600)).Check);
    }

    [Fact]
    public void The_total_limit_and_the_per_customer_limit_count_uses_so_far()
    {
        var coupon = Make();
        coupon.TotalLimit = 10;
        coupon.PerCustomerLimit = 1;

        Assert.Equal(CouponCheck.Valid, CouponRules.Check(coupon, Ctx(all: 9, mine: 0)).Check);
        Assert.Equal(CouponCheck.TotalLimitReached, CouponRules.Check(coupon, Ctx(all: 10)).Check);
        Assert.Equal(CouponCheck.CustomerLimitReached, CouponRules.Check(coupon, Ctx(all: 3, mine: 1)).Check);
    }

    [Fact]
    public void First_order_only_refuses_a_customer_who_has_ordered_before()
    {
        var coupon = Make();
        coupon.FirstOrderOnly = true;
        Assert.Equal(CouponCheck.FirstOrderOnly, CouponRules.Check(coupon, Ctx(hasOrders: true)).Check);
        Assert.Equal(CouponCheck.Valid, CouponRules.Check(coupon, Ctx(hasOrders: false)).Check);
    }

    [Fact]
    public void Free_delivery_on_an_order_that_already_ships_free_says_so()
    {
        var outcome = CouponRules.Check(Make(CouponKinds.FreeDelivery, 0), Ctx(delivery: 0));
        Assert.Equal(CouponCheck.NothingToDiscount, outcome.Check);
        Assert.Contains("already free", outcome.Message);
    }

    [Fact]
    public void After_a_store_reduces_the_order_a_percent_follows_the_new_total_and_never_goes_up()
    {
        var used = new CouponRedemption { CouponId = Guid.NewGuid(), OrderId = Guid.NewGuid(), UserId = Guid.NewGuid(), Kind = CouponKinds.Percent, Value = 10, MaxDiscount = 100, Amount = 50 };
        Assert.Equal(30m, CouponRules.After(used, 300, 30));
        Assert.Equal(50m, CouponRules.After(used, 900, 30)); // a dearer total cannot raise it above what was given
        Assert.Equal(0m, CouponRules.After(used, 0, 30));
    }

    [Fact]
    public void After_a_reduction_flat_is_capped_by_the_new_total_and_free_delivery_stays_with_the_fee()
    {
        var flat = new CouponRedemption { CouponId = Guid.NewGuid(), OrderId = Guid.NewGuid(), UserId = Guid.NewGuid(), Kind = CouponKinds.Flat, Value = 50, Amount = 50 };
        Assert.Equal(20m, CouponRules.After(flat, 20, 30));
        Assert.Equal(50m, CouponRules.After(flat, 400, 30));

        var delivery = new CouponRedemption { CouponId = Guid.NewGuid(), OrderId = Guid.NewGuid(), UserId = Guid.NewGuid(), Kind = CouponKinds.FreeDelivery, Value = 0, Amount = 30 };
        Assert.Equal(30m, CouponRules.After(delivery, 100, 30));
    }

    [Theory]
    [InlineData("SAVE10", "Percent", 10, null, 0, null, null, null, true)]
    [InlineData("sv", "Percent", 10, null, 0, null, null, null, false)]
    [InlineData("SAVE10", "Percent", 0, null, 0, null, null, null, false)]
    [InlineData("SAVE10", "Percent", 101, null, 0, null, null, null, false)]
    [InlineData("SAVE10", "Percent", 10, 100.0, 0, null, null, null, true)]
    [InlineData("SAVE10", "Flat", 50, 100.0, 0, null, null, null, false)] // only percent has a cap
    [InlineData("SAVE10", "Flat", 0, null, 0, null, null, null, false)]
    [InlineData("SAVE10", "FreeDelivery", 5, null, 0, null, null, null, false)]
    [InlineData("SAVE10", "FreeDelivery", 0, null, 199, null, null, null, true)]
    [InlineData("SAVE10", "Mystery", 10, null, 0, null, null, null, false)]
    [InlineData("SAVE10", "Percent", 10, null, -1, null, null, null, false)]
    [InlineData("SAVE10", "Percent", 10.123, null, 0, null, null, null, false)]
    [InlineData("SAVE10", "Percent", 10, null, 0, null, 0, null, false)]
    [InlineData("SAVE10", "Percent", 10, null, 0, null, null, 0, false)]
    [InlineData("SAVE10", "Percent", 10, null, 0, 1, null, null, true)]
    public void An_administrators_coupon_is_checked_before_it_is_saved(string code, string kind, double value, double? cap, double min, int? endsInDays, int? totalLimit, int? perCustomerLimit, bool ok)
    {
        DateTime? ends = endsInDays is { } d ? Now.AddDays(d) : null;
        var problem = CouponRules.Validate(code, kind, (decimal)value, (decimal?)cap, (decimal)min, Now, ends, totalLimit, perCustomerLimit);
        Assert.Equal(ok, problem is null);
    }

    [Fact]
    public void The_end_must_be_after_the_start()
    {
        Assert.NotNull(CouponRules.Validate("SAVE10", CouponKinds.Percent, 10, null, 0, Now, Now, null, null));
        Assert.Null(CouponRules.Validate("SAVE10", CouponKinds.Percent, 10, null, 0, Now, Now.AddSeconds(1), null, null));
    }
}
