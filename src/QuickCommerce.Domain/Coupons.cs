namespace QuickCommerce.Domain;

public static class CouponKinds
{
	/// <summary>A percent of the product subtotal, up to <see cref="Coupon.MaxDiscount"/> when one is set.</summary>
	public const string Percent = "Percent";

	/// <summary>A fixed amount in rupees, never more than the product subtotal.</summary>
	public const string Flat = "Flat";

	/// <summary>Takes off the delivery fee.</summary>
	public const string FreeDelivery = "FreeDelivery";

	public static readonly string[] All = [Percent, Flat, FreeDelivery];
}

/// <summary>
/// A code a customer types in the cart. Made by an administrator for the whole organisation; the business pays for it, so a store's
/// commission is not reduced by it.
/// </summary>
public sealed class Coupon
{
	public Guid Id { get; set; } = Guid.NewGuid();
	public Guid OrganizationId { get; set; }

	/// <summary>Stored upper-case without spaces; a customer may type it in any case.</summary>
	public required string Code { get; set; }
	public string? Description { get; set; }
	public required string Kind { get; set; }

	/// <summary>The percent (1 to 100) for Percent, the rupees for Flat, 0 for FreeDelivery.</summary>
	public decimal Value { get; set; }

	/// <summary>The most a Percent coupon takes off. Null means no cap.</summary>
	public decimal? MaxDiscount { get; set; }

	/// <summary>The product subtotal an order needs to use it. 0 means no minimum.</summary>
	public decimal MinSubtotal { get; set; }
	public DateTime StartsAt { get; set; } = DateTime.UtcNow;
	public DateTime? EndsAt { get; set; }

	/// <summary>How many orders may use it in all. Null means no limit.</summary>
	public int? TotalLimit { get; set; }

	/// <summary>How many times one customer may use it. Null means no limit.</summary>
	public int? PerCustomerLimit { get; set; }

	/// <summary>Only for a customer who has not placed an order before.</summary>
	public bool FirstOrderOnly { get; set; }

	/// <summary>Switched off by an administrator. A switched-off code cannot be used, but orders that already used it keep their discount.</summary>
	public bool IsActive { get; set; } = true;

	/// <summary>The card design (the same keys as offers). Null is the plain card.</summary>
	public string? Design { get; set; }

	/// <summary>A picture this API saved under /media/offers; it replaces the design's artwork.</summary>
	public string? ImageUrl { get; set; }

	/// <summary>A few words across the corner of the card, such as NEW.</summary>
	public string? Ribbon { get; set; }

	/// <summary>Listed to customers in the cart. Otherwise only someone who knows the code can use it.</summary>
	public bool ShowInApp { get; set; }

	/// <summary>Also shown once to each customer as a pop-up they can close, when the app opens. Implies <see cref="ShowInApp"/>.</summary>
	public bool ShowAsPopup { get; set; }

	/// <summary>Played in the app when the coupon is applied. Null is none.</summary>
	public string? Animation { get; set; }
	/// <summary>Orders using it now (uses given back are taken off). Kept as a number so the total limit can be enforced with one conditional update.</summary>
	public int UsedCount { get; set; }
	public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
	public string? CreatedBy { get; set; }
}

/// <summary>
/// One order's use of a coupon. Keeps what the coupon was when it was used, so a later edit of the coupon never changes an order, and so a
/// store's change to the order can work out the discount again. Released when the order is cancelled, rejected or expires unpaid, which
/// gives the use back.
/// </summary>
public sealed class CouponRedemption
{
	public Guid Id { get; set; } = Guid.NewGuid();
	public Guid CouponId { get; set; }
	public Guid OrderId { get; set; }
	public Guid UserId { get; set; }
	public required string Kind { get; set; }
	public decimal Value { get; set; }
	public decimal? MaxDiscount { get; set; }

	/// <summary>The discount the order has now (it can shrink when the store reduces the order).</summary>
	public decimal Amount { get; set; }
	public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

	/// <summary>Set when the use was given back. A released use no longer counts against the limits.</summary>
	public DateTime? ReleasedAt { get; set; }
}
