using QuickCommerce.Application.DTOs;
using QuickCommerce.Application.Caching;
using QuickCommerce.Application.Interfaces;
using QuickCommerce.Application.Validators;
using QuickCommerce.Domain;

namespace QuickCommerce.Application.Services;

public sealed class CatalogService(ICommerceStore data, IStoreSelectionService storeSelectionService, ICacheService cache, IDeliveryEstimator? deliveryEstimator = null) : ICatalogService
{
    private readonly IDeliveryEstimator estimator = deliveryEstimator ?? new DeliveryEstimator(new DeliverySettings());
    private static readonly TimeSpan CategoriesTtl = TimeSpan.FromMinutes(15);
    private static readonly TimeSpan ProductsTtl = TimeSpan.FromMinutes(5);
    private static readonly TimeSpan ProductTtl = TimeSpan.FromMinutes(10);
    private static readonly TimeSpan StoresTtl = TimeSpan.FromMinutes(10);
    private static readonly CatalogProductQueryValidator CatalogQueryValidator = new();
    private static readonly NearestStoreQueryValidator NearestQueryValidator = new();

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

    public async Task<CatalogProductPageResult> GetCatalogProductsAsync(CatalogProductQuery query, CancellationToken cancellationToken = default)
    {
        var validation = await CatalogQueryValidator.ValidateAsync(query, cancellationToken);
        if (!validation.IsValid)
        {
            return CatalogProductPageResult.Invalid(string.Join("; ", validation.Errors.Select(error => error.ErrorMessage)));
        }

        var search = string.IsNullOrWhiteSpace(query.Search) ? null : query.Search.Trim();

        if (query.CarriedOnly && query.StoreId is { } carriedStoreId)
        {
            // Per-store pages are not cached: they are cheap (primary-key lookup on the inventory table) and must reflect catalogue changes at once.
            if (!await IsActiveStoreAsync(carriedStoreId, cancellationToken))
            {
                return CatalogProductPageResult.StoreNotFound();
            }

            var (carried, carriedTotal) = await data.GetStoreProductPageAsync(carriedStoreId, search, query.CategoryId, (query.Page - 1) * query.PageSize, query.PageSize, cancellationToken);
            var carriedPage = new PagedResult<CatalogProductResponse>(await BuildForCustomerAsync(carried, cancellationToken), query.Page, query.PageSize, carriedTotal);
            return CatalogProductPageResult.Succeeded(carriedPage with { Items = await WithStockAsync(carriedPage.Items, carriedStoreId, cancellationToken) });
        }

        // Free-text searches are not cached because the key space is unbounded. Browsing by category and page is.
        // The cached page never contains stock: stock changes often, so it is added fresh below on every request.
        var cacheKey = search is null ? CatalogCacheKeys.CatalogPage(query.CategoryId, query.Page, query.PageSize) : null;
        var page = cacheKey is null ? null : await cache.GetAsync<PagedResult<CatalogProductResponse>>(cacheKey, cancellationToken);
        if (page is null)
        {
            var (items, total) = await data.GetActiveProductPageAsync(search, query.CategoryId, (query.Page - 1) * query.PageSize, query.PageSize, cancellationToken);
            page = new PagedResult<CatalogProductResponse>(await BuildForCustomerAsync(items, cancellationToken), query.Page, query.PageSize, total);
            if (cacheKey is not null)
            {
                await cache.SetAsync(cacheKey, page, ProductsTtl, cancellationToken);
            }
        }

        if (query.StoreId is { } storeId)
        {
            if (!await IsActiveStoreAsync(storeId, cancellationToken))
            {
                return CatalogProductPageResult.StoreNotFound();
            }

            page = page with { Items = await WithStockAsync(page.Items, storeId, cancellationToken) };
        }

        return CatalogProductPageResult.Succeeded(page);
    }

    public async Task<CatalogProductDetailResult> GetCatalogProductForStoreAsync(Guid id, Guid storeId, CancellationToken cancellationToken = default)
    {
        var product = await GetCatalogProductAsync(id, cancellationToken);
        if (product is null)
        {
            return new CatalogProductDetailResult(CatalogQueryStatus.NotFound, Message: "Product not found");
        }

        if (storeId == Guid.Empty || !await IsActiveStoreAsync(storeId, cancellationToken))
        {
            return new CatalogProductDetailResult(CatalogQueryStatus.StoreNotFound, Message: "Store not found");
        }

        return new CatalogProductDetailResult(CatalogQueryStatus.Succeeded, (await WithStockAsync([product], storeId, cancellationToken))[0]);
    }

    public async Task<NearestStoreResult> FindNearestStoreForCustomerAsync(NearestStoreQuery query, CancellationToken cancellationToken = default)
    {
        var validation = await NearestQueryValidator.ValidateAsync(query, cancellationToken);
        if (!validation.IsValid)
        {
            return NearestStoreResult.Invalid(string.Join("; ", validation.Errors.Select(error => error.ErrorMessage)));
        }

        // No product filter: the nearest store that serves the location, whatever it has in stock. Stock is reported per product.
        var store = storeSelectionService.FindNearest(query.Latitude, query.Longitude, [], await data.GetStoresAsync(cancellationToken), []);
        if (store is null)
        {
            return NearestStoreResult.None();
        }

        var distance = StoreSelectionService.DistanceKm(query.Latitude, query.Longitude, store.Latitude, store.Longitude);
        return NearestStoreResult.Succeeded(new NearestStoreResponse(store.Id, store.Name, Math.Round(distance, 1), estimator.EstimateMinutes(distance), store.ServiceRadiusKm));
    }

    public async Task<ServiceableStoreListResult> GetServiceableStoresAsync(NearestStoreQuery query, CancellationToken cancellationToken = default)
    {
        var validation = await NearestQueryValidator.ValidateAsync(query, cancellationToken);
        if (!validation.IsValid)
        {
            return ServiceableStoreListResult.Invalid(string.Join("; ", validation.Errors.Select(error => error.ErrorMessage)));
        }

        // A store that carries nothing would be a dead end for the customer, so it is not offered.
        var carrying = (await data.GetStoreIdsCarryingProductsAsync(cancellationToken)).ToHashSet();
        var stores = (await data.GetStoresAsync(cancellationToken))
            .Where(store => store.IsActive && carrying.Contains(store.Id))
            .Select(store => (Store: store, Distance: StoreSelectionService.DistanceKm(query.Latitude, query.Longitude, store.Latitude, store.Longitude)))
            .Where(entry => entry.Distance <= entry.Store.ServiceRadiusKm)
            .OrderBy(entry => entry.Distance)
            .ThenBy(entry => entry.Store.Name, StringComparer.OrdinalIgnoreCase)
            .Select(entry => new ServiceableStoreResponse(entry.Store.Id, entry.Store.Name, entry.Store.Address, Math.Round(entry.Distance, 1), estimator.EstimateMinutes(entry.Distance), entry.Store.ServiceRadiusKm))
            .ToArray();
        return ServiceableStoreListResult.Succeeded(stores);
    }

    public async Task<CatalogCategoryListResult> GetCatalogCategoriesForStoreAsync(Guid storeId, CancellationToken cancellationToken = default)
    {
        if (storeId == Guid.Empty || !await IsActiveStoreAsync(storeId, cancellationToken))
        {
            return new CatalogCategoryListResult(CatalogQueryStatus.StoreNotFound, Message: "Store not found");
        }

        var all = await GetCatalogCategoriesAsync(cancellationToken);
        var keep = (await data.GetCarriedCategoryIdsAsync(storeId, cancellationToken)).ToHashSet();
        var byId = all.ToDictionary(category => category.Id);
        // A sub-category the store carries keeps its parent visible.
        foreach (var id in keep.ToArray())
        {
            for (var parent = byId.GetValueOrDefault(id)?.ParentCategoryId; parent is { } parentId && keep.Add(parentId); parent = byId.GetValueOrDefault(parentId)?.ParentCategoryId)
            {
            }
        }

        return new CatalogCategoryListResult(CatalogQueryStatus.Succeeded, all.Where(category => keep.Contains(category.Id)).ToArray());
    }

    private async Task<bool> IsActiveStoreAsync(Guid storeId, CancellationToken cancellationToken) =>
        (await data.GetStoresAsync(cancellationToken)).Any(store => store.Id == storeId && store.IsActive);

    private async Task<IReadOnlyList<CatalogProductResponse>> WithStockAsync(IReadOnlyList<CatalogProductResponse> items, Guid storeId, CancellationToken cancellationToken)
    {
        if (items.Count == 0)
        {
            return items;
        }

        var variantIds = items.SelectMany(item => item.Variants ?? []).Select(variant => variant.Id).ToArray();
        var stock = (await data.GetStoreVariantInventoryAsync(storeId, variantIds, cancellationToken)).ToDictionary(row => row.VariantId);
        return items.Select(item => ApplyStock(item, stock)).ToArray();
    }

    // A store with no stock row for a variant does not carry it. Only flags leave the API, never quantities.
    // The product-level flags describe the default variant, which is what a client that ignores variants adds to its cart.
    private static CatalogProductResponse ApplyStock(CatalogProductResponse product, IReadOnlyDictionary<Guid, StoreVariantInventory> stock)
    {
        var variants = (product.Variants ?? []).Select(variant =>
        {
            stock.TryGetValue(variant.Id, out var row);
            var inStock = row is { AvailableQuantity: > 0 };
            return variant with { InStock = inStock, LowStock = inStock && row!.AvailableQuantity <= row.ReorderThreshold };
        }).ToArray();
        var defaultVariant = variants.FirstOrDefault(variant => variant.IsDefault);
        return product with { Variants = variants, InStock = defaultVariant?.InStock ?? false, LowStock = defaultVariant?.LowStock ?? false };
    }

    public async Task<IReadOnlyList<CatalogCategoryResponse>> GetCatalogCategoriesAsync(CancellationToken cancellationToken = default)
    {
        var cached = await cache.GetAsync<CatalogCategoryResponse[]>(CatalogCacheKeys.CatalogCategories, cancellationToken);
        if (cached is not null)
        {
            return cached;
        }

        var translations = (await data.GetCategoryTranslationsAsync(cancellationToken)).ToLookup(translation => translation.CategoryId);
        var result = (await data.GetCategoriesAsync(cancellationToken))
            .Where(category => category.IsActive)
            .Select(category => new CatalogCategoryResponse(
                category.Id,
                category.Name,
                category.ParentCategoryId,
                translations[category.Id]
                    .GroupBy(translation => translation.Locale.ToLowerInvariant())
                    .ToDictionary(group => group.Key, group => new CatalogCategoryTranslation(group.First().Name))))
            .ToArray();
        await cache.SetAsync(CatalogCacheKeys.CatalogCategories, result, CategoriesTtl, cancellationToken);
        return result;
    }

    public async Task<CatalogProductResponse?> GetCatalogProductAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var key = CatalogCacheKeys.CatalogProduct(id);
        var cached = await cache.GetAsync<CatalogProductResponse>(key, cancellationToken);
        if (cached is not null)
        {
            return cached;
        }

        var product = await data.GetProductAsync(id, cancellationToken);
        if (product is null || !product.IsActive)
        {
            return null;
        }

        var result = (await BuildForCustomerAsync([product], cancellationToken))[0];
        await cache.SetAsync(key, result, ProductTtl, cancellationToken);
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
        var inventory = await data.GetVariantInventoryAsync(cancellationToken);
        var defaultVariantIds = (await data.GetVariantsAsync(productIds.ToArray(), cancellationToken)).Where(variant => variant.IsDefault && variant.IsActive).Select(variant => variant.Id).ToArray();
        var store = storeSelectionService.FindNearest(latitude, longitude, defaultVariantIds, stores, inventory);
        return store is null ? null : Map(store);
    }

    private async Task<CatalogProductResponse[]> BuildForCustomerAsync(IReadOnlyList<Product> products, CancellationToken cancellationToken)
    {
        if (products.Count == 0)
        {
            return [];
        }

        var ids = products.Select(product => product.Id).ToArray();
        var translations = (await data.GetProductTranslationsAsync(ids, cancellationToken)).ToLookup(translation => translation.ProductId);
        var variants = (await data.GetVariantsAsync(ids, cancellationToken)).ToLookup(variant => variant.ProductId);
        return products.Select(product => MapForCustomer(product, translations[product.Id], variants[product.Id])).ToArray();
    }

    /// <summary>
    /// The product's own price, MRP and unit describe its default variant, so a client written before variants sees what it always saw.
    /// A product without any active variant falls back to its own columns.
    /// </summary>
    private static CatalogProductResponse MapForCustomer(Product product, IEnumerable<ProductTranslation> translations, IEnumerable<ProductVariant> variants)
    {
        var active = variants.Where(variant => variant.IsActive).OrderBy(variant => variant.SortOrder).ThenBy(variant => variant.Price).ToArray();
        var defaultVariant = active.FirstOrDefault(variant => variant.IsDefault) ?? active.FirstOrDefault();
        var price = defaultVariant?.Price ?? product.Price;
        var mrp = defaultVariant is null ? product.Mrp : defaultVariant.Mrp;
        return new CatalogProductResponse(
            product.Id,
            product.Name,
            product.Description,
            price,
            CatalogPricing.EffectiveMrp(price, mrp),
            CatalogPricing.DiscountPercent(price, mrp),
            defaultVariant?.Label ?? product.UnitOfMeasure,
            product.CategoryId,
            product.ImageUrl,
            translations
                .GroupBy(translation => translation.Locale.ToLowerInvariant())
                .ToDictionary(group => group.Key, group => new CatalogTranslation(group.First().Name, group.First().Description)),
            Variants: active.Select(variant => new CatalogVariantResponse(
                variant.Id,
                variant.Label,
                variant.Price,
                CatalogPricing.EffectiveMrp(variant.Price, variant.Mrp),
                CatalogPricing.DiscountPercent(variant.Price, variant.Mrp),
                variant.Id == defaultVariant?.Id)).ToArray());
    }

    private static CategoryResponse Map(Category category) => new(category.Id, category.Name, category.ParentCategoryId, category.IsActive);

    private static ProductResponse Map(Product product) => new(product.Id, product.Sku, product.Name, product.Description, product.Price, product.UnitOfMeasure, product.CategoryId, product.ImageUrl, product.IsActive);

    private static StoreResponse Map(Store store) => new(store.Id, store.Name, store.Address, store.Latitude, store.Longitude, store.ServiceRadiusKm, store.IsActive);
}
