using QuickCommerce.Domain;

namespace QuickCommerce.Application.Services;

public enum CouponCheck
{
    Valid,
    NotFound,
    Inactive,
    NotStarted,
    Expired,
    BelowMinimum,
    TotalLimitReached,
    CustomerLimitReached,
    FirstOrderOnly,
    NothingToDiscount,
}

/// <summary>What is known about the order when a code is tried.</summary>
/// <param name="Subtotal">The product total after offer and store-price discounts.</param>
/// <param name="DeliveryFee">The delivery fee the order would be charged.</param>
/// <param name="UsesInAll">How many orders have used the coupon and not given it back.</param>
/// <param name="UsesByCustomer">How many of those are this customer's.</param>
/// <param name="CustomerHasOrders">Whether this customer has placed an order before.</param>
public sealed record CouponContext(decimal Subtotal, decimal DeliveryFee, DateTime Now, int UsesInAll, int UsesByCustomer, bool CustomerHasOrders);

public sealed record CouponOutcome(CouponCheck Check, decimal Discount, string Message);

/// <summary>
/// The rules for a coupon, written once. The cart shows the result and checkout applies the same one inside the order's transaction, so
/// what the customer was told is what they pay. Money is in rupees, rounded to paise away from zero.
/// </summary>
public static class CouponRules
{
    public const int CodeMin = 3;
    public const int CodeMax = 20;

    /// <summary>What is stored and compared: no spaces, upper-case.</summary>
    public static string Normalize(string? code) => new string((code ?? string.Empty).Where(c => !char.IsWhiteSpace(c)).ToArray()).ToUpperInvariant();

    public static bool IsValidCode(string code) =>
        code.Length is >= CodeMin and <= CodeMax && code.All(c => c is (>= 'A' and <= 'Z') or (>= '0' and <= '9') or '-' or '_');

    /// <summary>The discount for these amounts, or 0. Percent is capped, flat never exceeds the product total, free delivery is the delivery fee.</summary>
    public static decimal Discount(string kind, decimal value, decimal? maxDiscount, decimal subtotal, decimal deliveryFee)
    {
        if (subtotal <= 0)
        {
            return 0;
        }

        var amount = kind switch
        {
            CouponKinds.Percent => Math.Round(subtotal * value / 100m, 2, MidpointRounding.AwayFromZero),
            CouponKinds.Flat => value,
            CouponKinds.FreeDelivery => deliveryFee,
            _ => 0m,
        };
        if (kind == CouponKinds.Percent && maxDiscount is { } cap)
        {
            amount = Math.Min(amount, cap);
        }

        // A coupon never makes the products free, and never pays back more than the delivery fee it covers.
        return Math.Max(0, kind == CouponKinds.FreeDelivery ? Math.Min(amount, deliveryFee) : Math.Min(amount, subtotal));
    }

    public static CouponOutcome Check(Coupon? coupon, CouponContext context)
    {
        if (coupon is null)
        {
            return new(CouponCheck.NotFound, 0, "That code is not valid.");
        }

        if (!coupon.IsActive)
        {
            return new(CouponCheck.Inactive, 0, "That code is no longer available.");
        }

        if (coupon.StartsAt > context.Now)
        {
            return new(CouponCheck.NotStarted, 0, "That code is not active yet.");
        }

        if (coupon.EndsAt is { } ends && ends <= context.Now)
        {
            return new(CouponCheck.Expired, 0, "That code has expired.");
        }

        if (coupon.FirstOrderOnly && context.CustomerHasOrders)
        {
            return new(CouponCheck.FirstOrderOnly, 0, "That code is only for a first order.");
        }

        if (coupon.TotalLimit is { } total && context.UsesInAll >= total)
        {
            return new(CouponCheck.TotalLimitReached, 0, "That code has been used up.");
        }

        if (coupon.PerCustomerLimit is { } each && context.UsesByCustomer >= each)
        {
            return new(CouponCheck.CustomerLimitReached, 0, "You have already used that code.");
        }

        if (context.Subtotal < coupon.MinSubtotal)
        {
            return new(CouponCheck.BelowMinimum, 0, $"Add items worth ₹{coupon.MinSubtotal - context.Subtotal:0.##} more to use that code.");
        }

        var discount = Discount(coupon.Kind, coupon.Value, coupon.MaxDiscount, context.Subtotal, context.DeliveryFee);
        return discount <= 0
            ? new(CouponCheck.NothingToDiscount, 0, coupon.Kind == CouponKinds.FreeDelivery ? "Delivery is already free on this order." : "That code does not change this order.")
            : new(CouponCheck.Valid, discount, "Code applied.");
    }

    /// <summary>
    /// The discount again after a store reduced the order. It can only go down: percent follows the new product total (same cap), flat
    /// is capped by it, free delivery stays as long as the fee does. The minimum order is not checked again.
    /// </summary>
    public static decimal After(CouponRedemption redemption, decimal newSubtotal, decimal deliveryFee) =>
        Math.Min(redemption.Amount, Discount(redemption.Kind, redemption.Value, redemption.MaxDiscount, newSubtotal, deliveryFee));

    /// <summary>The checks on a coupon an administrator is making or changing. Null when it is fine.</summary>
    public static string? Validate(string code, string kind, decimal value, decimal? maxDiscount, decimal minSubtotal, DateTime startsAt, DateTime? endsAt, int? totalLimit, int? perCustomerLimit)
    {
        if (!IsValidCode(code))
        {
            return $"The code must be {CodeMin} to {CodeMax} letters, digits, dashes or underscores.";
        }

        if (!CouponKinds.All.Contains(kind))
        {
            return "The kind must be Percent, Flat or FreeDelivery.";
        }

        if (decimal.Round(value, 2) != value || decimal.Round(minSubtotal, 2) != minSubtotal || (maxDiscount is { } cap && decimal.Round(cap, 2) != cap))
        {
            return "Amounts can have at most 2 decimals.";
        }

        switch (kind)
        {
            case CouponKinds.Percent when value is <= 0 or > 100:
                return "A percent coupon must be between 1 and 100 percent.";
            case CouponKinds.Flat when value is <= 0 or > 100_000:
                return "A flat coupon must be more than 0 rupees (and at most 1,00,000).";
            case CouponKinds.FreeDelivery when value != 0:
                return "A free delivery coupon has no amount.";
        }

        if (maxDiscount is { } max && (kind != CouponKinds.Percent || max <= 0))
        {
            return "Only a percent coupon can have a cap, and it must be more than 0.";
        }

        if (minSubtotal is < 0 or > 1_000_000)
        {
            return "The minimum order must be between 0 and 10,00,000 rupees.";
        }

        if (endsAt is { } end && end <= startsAt)
        {
            return "The end time must be after the start time.";
        }

        if (totalLimit is <= 0 || perCustomerLimit is <= 0)
        {
            return "A use limit must be 1 or more, or empty for no limit.";
        }

        return null;
    }
}
