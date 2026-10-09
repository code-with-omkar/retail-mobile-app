using QuickCommerce.Application.DTOs;

namespace QuickCommerce.Application.Interfaces;

public interface ICatalogService
{
    Task<IReadOnlyList<CategoryResponse>> GetCategoriesAsync(CancellationToken cancellationToken = default);
    Task<IReadOnlyList<ProductResponse>> GetProductsAsync(string? search, Guid? categoryId, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<CatalogCategoryResponse>> GetCatalogCategoriesAsync(CancellationToken cancellationToken = default);
    Task<CatalogProductResponse?> GetCatalogProductAsync(Guid id, CancellationToken cancellationToken = default);
    Task<NearestStoreResult> FindNearestStoreForCustomerAsync(NearestStoreQuery query, CancellationToken cancellationToken = default);
    Task<CatalogProductDetailResult> GetCatalogProductForStoreAsync(Guid id, Guid storeId, CancellationToken cancellationToken = default);
    /// <summary>Categories that have at least one product the store carries (and their parents).</summary>
    Task<CatalogCategoryListResult> GetCatalogCategoriesForStoreAsync(Guid storeId, CancellationToken cancellationToken = default);
    /// <summary>Stores that deliver to a location and carry products, nearest first.</summary>
    Task<ServiceableStoreListResult> GetServiceableStoresAsync(NearestStoreQuery query, CancellationToken cancellationToken = default);
    Task<CatalogProductPageResult> GetCatalogProductsAsync(CatalogProductQuery query, CancellationToken cancellationToken = default);
    Task<ProductResponse?> GetProductAsync(Guid id, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<StoreResponse>> GetStoresAsync(CancellationToken cancellationToken = default);
    Task<StoreResponse?> FindNearestStoreAsync(double latitude, double longitude, IReadOnlyCollection<Guid> productIds, CancellationToken cancellationToken = default);
}
