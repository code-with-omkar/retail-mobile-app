using QuickCommerce.Application.DTOs;

namespace QuickCommerce.Application.Interfaces;

public interface ICatalogService
{
    Task<IReadOnlyList<CategoryResponse>> GetCategoriesAsync(CancellationToken cancellationToken = default);
    Task<IReadOnlyList<ProductResponse>> GetProductsAsync(string? search, Guid? categoryId, CancellationToken cancellationToken = default);
    Task<ProductResponse?> GetProductAsync(Guid id, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<StoreResponse>> GetStoresAsync(CancellationToken cancellationToken = default);
    Task<StoreResponse?> FindNearestStoreAsync(double latitude, double longitude, IReadOnlyCollection<Guid> productIds, CancellationToken cancellationToken = default);
}
