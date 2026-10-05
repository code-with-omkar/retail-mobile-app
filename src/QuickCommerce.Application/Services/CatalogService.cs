using QuickCommerce.Application.DTOs;
using QuickCommerce.Application.Caching;
using QuickCommerce.Application.Interfaces;
using QuickCommerce.Domain;

namespace QuickCommerce.Application.Services;

public sealed class CatalogService(ICommerceStore data, IStoreSelectionService storeSelectionService, ICacheService cache) : ICatalogService
{
    private static readonly TimeSpan CategoriesTtl = TimeSpan.FromMinutes(15);
    private static readonly TimeSpan ProductsTtl = TimeSpan.FromMinutes(5);
    private static readonly TimeSpan ProductTtl = TimeSpan.FromMinutes(10);
    private static readonly TimeSpan StoresTtl = TimeSpan.FromMinutes(10);

    public async Task<IReadOnlyList<CategoryResponse>> GetCategoriesAsync(CancellationToken cancellationToken = default)
    {
        var key = CatalogCacheKeys.Categories;
        var cached = await cache.GetAsync<CategoryResponse[]>(key, cancellationToken);
        if (cached is not null)
        {
            return cached;
        }

        var result = (await data.GetCategoriesAsync(cancellationToken))
            .Where(category => category.IsActive)
            .Select(Map)
            .ToArray();
        await cache.SetAsync(key, result, CategoriesTtl, cancellationToken);
        return result;
    }

    public async Task<IReadOnlyList<ProductResponse>> GetProductsAsync(string? search, Guid? categoryId, CancellationToken cancellationToken = default)
    {
        Console.WriteLine($"GetProductsAsync called with search: {search}, categoryId: {categoryId}");
        var key = CatalogCacheKeys.Products(search, categoryId);
        var cached = await cache.GetAsync<ProductResponse[]>(key, cancellationToken);
        if (cached is not null)
        {
            return cached;
        }

        var products = (await data.GetProductsAsync(search, categoryId, cancellationToken)).Where(product => product.IsActive);
        if (!string.IsNullOrWhiteSpace(search))
        {
            products = products.Where(product => product.Name.Contains(search, StringComparison.OrdinalIgnoreCase) || product.Description.Contains(search, StringComparison.OrdinalIgnoreCase));
        }

        if (categoryId.HasValue)
        {
            products = products.Where(product => product.CategoryId == categoryId);
        }

        var result = products.Select(Map).ToArray();
        await cache.SetAsync(key, result, ProductsTtl, cancellationToken);
        return result;
    }

    public async Task<ProductResponse?> GetProductAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var key = CatalogCacheKeys.Product(id);
        var cached = await cache.GetAsync<ProductResponse>(key, cancellationToken);
        if (cached is not null)
        {
            return cached;
        }

        var product = await data.GetProductAsync(id, cancellationToken);
        if (product is null || !product.IsActive)
        {
            return null;
        }

        var result = Map(product);
        await cache.SetAsync(key, result, ProductTtl, cancellationToken);
        return result;
    }

    public async Task<IReadOnlyList<StoreResponse>> GetStoresAsync(CancellationToken cancellationToken = default)
    {
        var key = CatalogCacheKeys.Stores;
        var cached = await cache.GetAsync<StoreResponse[]>(key, cancellationToken);
        if (cached is not null)
        {
            return cached;
        }

        var result = (await data.GetStoresAsync(cancellationToken))
            .Where(store => store.IsActive)
            .Select(Map)
            .ToArray();
        await cache.SetAsync(key, result, StoresTtl, cancellationToken);
        return result;
    }

    public async Task<StoreResponse?> FindNearestStoreAsync(double latitude, double longitude, IReadOnlyCollection<Guid> productIds, CancellationToken cancellationToken = default)
    {
        var stores = await data.GetStoresAsync(cancellationToken);
        var inventory = await data.GetInventoryAsync(cancellationToken);
        var store = storeSelectionService.FindNearest(latitude, longitude, productIds, stores, inventory);
        return store is null ? null : Map(store);
    }

    private static CategoryResponse Map(Category category) => new(category.Id, category.Name, category.ParentCategoryId, category.IsActive);

    private static ProductResponse Map(Product product) => new(product.Id, product.Sku, product.Name, product.Description, product.Price, product.UnitOfMeasure, product.CategoryId, product.ImageUrl, product.IsActive);

    private static StoreResponse Map(Store store) => new(store.Id, store.Name, store.Address, store.Latitude, store.Longitude, store.ServiceRadiusKm, store.IsActive);
}
