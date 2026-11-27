namespace QuickCommerce.Domain;

/// <summary>Where an ad can be shown in the customer app. The keys are the same in the API, the portal and the app.</summary>
public static class AdPlacements
{
	public const string Home = "home";
	public const string HomeMiddle = "home_middle";
	public const string Categories = "categories";
	public const string Search = "search";
	public const string Cart = "cart";
	public const string Order = "order";
	public const string Product = "product";

	public static readonly string[] All = [Home, HomeMiddle, Categories, Search, Cart, Order, Product];
}

/// <summary>What a tap on an ad opens.</summary>
public static class AdTargets
{
	public const string None = "None";
	public const string Product = "Product";
	public const string Category = "Category";
	public const string Offer = "Offer";
	public const string Url = "Url";

	public static readonly string[] All = [None, Product, Category, Offer, Url];
}

/// <summary>
/// A picture the administrator puts in the customer app at one place (see <see cref="AdPlacements"/>), for one store or for all of them,
/// between two dates. Views and taps are counted.
/// </summary>
public sealed class Ad
{
	public Guid Id { get; set; } = Guid.NewGuid();
	public Guid OrganizationId { get; set; }
	public required string Placement { get; set; }
	public string? Title { get; set; }

	/// <summary>Always a path of our own picture store (an uploaded file), never an outside address.</summary>
	public required string ImageUrl { get; set; }
	public string TargetType { get; set; } = AdTargets.None;

	/// <summary>An id for Product, Category and Offer; an https address for Url; empty for None.</summary>
	public string? Target { get; set; }

	/// <summary>Null means every store of the organisation.</summary>
	public Guid? StoreId { get; set; }
	public DateTime StartsAt { get; set; } = DateTime.UtcNow;
	public DateTime? EndsAt { get; set; }
	public bool IsActive { get; set; } = true;

	/// <summary>Lower first when several ads share a place.</summary>
	public int SortOrder { get; set; }
	public long ViewCount { get; set; }
	public long ClickCount { get; set; }
	public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
	public string? CreatedBy { get; set; }
	public DateTime? UpdatedAt { get; set; }
}
