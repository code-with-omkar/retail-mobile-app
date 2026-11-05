namespace QuickCommerce.Domain;

/// <summary>
/// One step a customer took in the app, tied only to a random session id (made fresh each time the app starts), never to an account, phone,
/// email or address. Kept for 90 days; daily totals are kept for good in <see cref="AnalyticsDaily"/>.
/// </summary>
public sealed class AnalyticsEvent
{
	public long Id { get; set; }

	/// <summary>One of the names in <see cref="AnalyticsNames"/>.</summary>
	public required string Name { get; set; }
	public DateTime OccurredAt { get; set; } = DateTime.UtcNow;
	public required string SessionId { get; set; }
	public Guid? StoreId { get; set; }

	/// <summary>A few safe words that say more about the step (a search term, why a code was refused). Never free personal text.</summary>
	public string? Detail { get; set; }
}

/// <summary>One day's totals for one step (and store, or none): how many times and in how many sessions. Written from the raw events before they are purged.</summary>
public sealed class AnalyticsDaily
{
	public DateOnly Day { get; set; }
	public required string Name { get; set; }

	/// <summary>The store, or <see cref="Guid.Empty"/> for steps with no store.</summary>
	public Guid StoreKey { get; set; }
	public int Events { get; set; }
	public int Sessions { get; set; }
}

public static class AnalyticsNames
{
	public const string AppOpen = "app_open";
	public const string Search = "search";
	public const string ProductView = "product_view";
	public const string AddToCart = "add_to_cart";
	public const string CartOpen = "cart_open";
	public const string CheckoutStart = "checkout_start";
	public const string CouponApplied = "coupon_applied";
	public const string CouponRefused = "coupon_refused";
	public const string SlotChosen = "slot_chosen";
	public const string OrderPlaced = "order_placed";
	public const string StoreChosen = "store_chosen";
	public const string OutOfServiceArea = "out_of_service_area";

	public static readonly string[] All =
	[
		AppOpen, Search, ProductView, AddToCart, CartOpen, CheckoutStart, CouponApplied, CouponRefused, SlotChosen, OrderPlaced, StoreChosen, OutOfServiceArea,
	];

	/// <summary>The steps of the buying funnel, in order.</summary>
	public static readonly string[] Funnel = [AppOpen, ProductView, AddToCart, CheckoutStart, OrderPlaced];
}
