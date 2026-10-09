namespace QuickCommerce.Application.DTOs;

public sealed record CategoryResponse(Guid Id, string Name, Guid? ParentCategoryId, bool IsActive);

public sealed record ProductResponse(
    Guid Id,
    string Sku,
    string Name,
    string Description,
    decimal Price,
    string UnitOfMeasure,
    Guid CategoryId,
    string? ImageUrl,
    bool IsActive);

public sealed record StoreResponse(
    Guid Id,
    string Name,
    string Address,
    double Latitude,
    double Longitude,
    double ServiceRadiusKm,
    bool IsActive);

/// <summary>Product as shown to customers. Deliberately has no SKU or IsActive; it can grow (MRP, translations, availability) without changing the admin contract.</summary>
/// <param name="Mrp">Maximum retail price. Always present: equals the price when the product has no separate MRP.</param>
/// <param name="DiscountPercent">Rounded percentage between MRP and price; 0 when there is no discount.</param>
/// <param name="Translations">Translated text keyed by lowercase language code. Only languages that have a translation appear.</param>
/// <param name="InStock">Stock at the requested store; null when no store was requested. Never an exact quantity.</param>
/// <param name="LowStock">True when in stock but at or below the store's reorder threshold; null when no store was requested.</param>
public sealed record CatalogProductResponse(
    Guid Id,
    string Name,
    string Description,
    decimal Price,
    decimal Mrp,
    int DiscountPercent,
    string UnitOfMeasure,
    Guid CategoryId,
    string? ImageUrl,
    IReadOnlyDictionary<string, CatalogTranslation> Translations,
    bool? InStock = null,
    bool? LowStock = null,
    IReadOnlyList<CatalogVariantResponse>? Variants = null);

/// <summary>One pack size of a product. The product's own price, MRP and flags describe its default variant.</summary>
/// <param name="Mrp">Always present: equals the price when the variant has no separate MRP.</param>
/// <param name="InStock">Stock of this variant at the requested store; null when no store was requested. Never a quantity.</param>
public sealed record CatalogVariantResponse(
    Guid Id,
    string Label,
    decimal Price,
    decimal Mrp,
    int DiscountPercent,
    bool IsDefault,
    bool? InStock = null,
    bool? LowStock = null);

/// <param name="Description">Null means "use the English description".</param>
public sealed record CatalogTranslation(string Name, string? Description);

public sealed record CatalogCategoryTranslation(string Name);

public sealed record CatalogCategoryResponse(
    Guid Id,
    string Name,
    Guid? ParentCategoryId,
    IReadOnlyDictionary<string, CatalogCategoryTranslation> Translations);

public sealed record PagedResult<T>(IReadOnlyList<T> Items, int Page, int PageSize, int TotalCount)
{
    public bool HasMore => (long)Page * PageSize < TotalCount;
}

/// <param name="CarriedOnly">With a <paramref name="StoreId"/>: only products that store carries (has an inventory row for). Without it the store only adds stock flags.</param>
public sealed record CatalogProductQuery(string? Search, Guid? CategoryId, int Page = 1, int PageSize = CatalogProductQuery.DefaultPageSize, Guid? StoreId = null, bool CarriedOnly = false)
{
    public const int DefaultPageSize = 20;
    public const int MaxPageSize = 50;
    public const int MaxSearchLength = 100;
}

public enum CatalogQueryStatus
{
    Succeeded,
    InvalidRequest,
    StoreNotFound,
    NotFound
}

public sealed record CatalogProductPageResult(CatalogQueryStatus Status, PagedResult<CatalogProductResponse>? Page = null, string? Message = null)
{
    public static CatalogProductPageResult Succeeded(PagedResult<CatalogProductResponse> page) => new(CatalogQueryStatus.Succeeded, page);
    public static CatalogProductPageResult Invalid(string message) => new(CatalogQueryStatus.InvalidRequest, Message: message);
    public static CatalogProductPageResult StoreNotFound() => new(CatalogQueryStatus.StoreNotFound, Message: "Store not found");
}

public sealed record CatalogProductDetailResult(CatalogQueryStatus Status, CatalogProductResponse? Product = null, string? Message = null);

/// <summary>Nearest store that can serve a location. Coordinates of the store are not repeated here.</summary>
public sealed record NearestStoreResponse(Guid Id, string Name, double DistanceKm, int EstimatedMinutes, double ServiceRadiusKm);

public sealed record NearestStoreQuery(double Latitude, double Longitude);

/// <summary>A store that delivers to a location, for the customer's store picker.</summary>
public sealed record ServiceableStoreResponse(Guid Id, string Name, string Address, double DistanceKm, int EstimatedMinutes, double ServiceRadiusKm);

/// <summary>An empty list is a valid answer: no store delivers there.</summary>
public sealed record ServiceableStoreListResult(bool Valid, IReadOnlyList<ServiceableStoreResponse> Stores, string? Message = null)
{
    public static ServiceableStoreListResult Succeeded(IReadOnlyList<ServiceableStoreResponse> stores) => new(true, stores);
    public static ServiceableStoreListResult Invalid(string message) => new(false, [], message);
}

public sealed record CatalogCategoryListResult(CatalogQueryStatus Status, IReadOnlyList<CatalogCategoryResponse>? Categories = null, string? Message = null);

public enum NearestStoreStatus
{
    Succeeded,
    InvalidRequest,
    NoServiceableStore
}

public sealed record NearestStoreResult(NearestStoreStatus Status, NearestStoreResponse? Store = null, string? Message = null)
{
    public static NearestStoreResult Succeeded(NearestStoreResponse store) => new(NearestStoreStatus.Succeeded, store);
    public static NearestStoreResult Invalid(string message) => new(NearestStoreStatus.InvalidRequest, Message: message);
    public static NearestStoreResult None() => new(NearestStoreStatus.NoServiceableStore, Message: "No serviceable store near this location");
}
