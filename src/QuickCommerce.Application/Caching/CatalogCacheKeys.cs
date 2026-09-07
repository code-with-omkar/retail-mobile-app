namespace QuickCommerce.Application.Caching;

public static class CatalogCacheKeys
{
    public const string Categories = "quickcommerce:catalog:categories:v1";
    public const string ProductsPrefix = "quickcommerce:catalog:products:v1";
    public const string Stores = "quickcommerce:catalog:stores:v1";
    public const string ProductPrefix = "quickcommerce:catalog:product:v1";

    public static string Products(string? search, Guid? categoryId) =>
        $"{ProductsPrefix}:search={Normalize(search)}:category={categoryId?.ToString() ?? "all"}";

    public static string Product(Guid id) => $"{ProductPrefix}:{id}";

    private static string Normalize(string? value) => string.IsNullOrWhiteSpace(value)
        ? "all"
        : Uri.EscapeDataString(value.Trim().ToLowerInvariant());
}