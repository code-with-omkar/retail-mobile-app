namespace QuickCommerce.Application.Caching;

public static class CatalogCacheKeys
{
    public const string Categories = "quickcommerce:catalog:categories:v1";
    public const string ProductsPrefix = "quickcommerce:catalog:products:v1";
    public const string Stores = "quickcommerce:catalog:stores:v1";
    public const string ProductPrefix = "quickcommerce:catalog:product:v1";

    public static string Products(string? search, Guid? categoryId) =>
        $"{ProductsPrefix}:search={Normalize(search)}:category={categoryId?.ToString() ?? "all"}";

    public const string CatalogPagePrefix = "quickcommerce:catalog:page:v3";

    public static string CatalogPage(Guid? categoryId, int page, int pageSize) =>
        $"{CatalogPagePrefix}:category={categoryId?.ToString() ?? "all"}:page={page}:size={pageSize}";

    public const string CatalogCategories = "quickcommerce:catalog:customer-categories:v1";
    public const string CatalogProductPrefix = "quickcommerce:catalog:customer-product:v1";

    public static string CatalogProduct(Guid id) => $"{CatalogProductPrefix}:{id}";

    public static string Product(Guid id) => $"{ProductPrefix}:{id}";

    private static string Normalize(string? value) => string.IsNullOrWhiteSpace(value)
        ? "all"
        : Uri.EscapeDataString(value.Trim().ToLowerInvariant());
}