using QuickCommerce.Domain;

namespace QuickCommerce.Application.Interfaces;

public sealed record AdminVariantResponse(Guid Id, string Sku, string Label, decimal Price, decimal? Mrp, int SortOrder, bool IsDefault, bool IsActive);

public sealed record AdminTranslationResponse(string Locale, string Name, string? Description);

public sealed record AdminProductResponse(
    Guid Id,
    string Sku,
    string Name,
    string Description,
    Guid CategoryId,
    string CategoryName,
    string UnitOfMeasure,
    string? ImageUrl,
    bool IsActive,
    decimal Price,
    decimal? Mrp,
    IReadOnlyList<AdminVariantResponse> Variants,
    IReadOnlyList<AdminTranslationResponse> Translations);

public sealed record AdminProductPage(IReadOnlyList<AdminProductResponse> Items, int Page, int PageSize, int TotalCount);

/// <summary>A new product also gets its first (default) variant, labelled with the unit, at this price.</summary>
public sealed record CreateProductRequest(string Sku, string Name, string Description, Guid CategoryId, string UnitOfMeasure, decimal Price, decimal? Mrp = null);

public sealed record UpdateProductRequest(string Name, string Description, Guid CategoryId, string UnitOfMeasure, bool IsActive);

public sealed record SaveTranslationRequest(string Name, string? Description = null);

public sealed record SaveVariantRequest(string Sku, string Label, decimal Price, decimal? Mrp, int SortOrder = 0, bool IsActive = true);

public sealed record StockRowResponse(Guid ProductId, string ProductName, Guid VariantId, string VariantLabel, bool Carried, int Available, int ReorderThreshold);

/// <summary>Set whether the store carries a variant and how many it has. Not carried removes the stock row.</summary>
public sealed record SetStockRequest(bool Carried, int Available, int ReorderThreshold = 5);

public enum CatalogAdminStatus { Succeeded, Forbidden, NotFound, Invalid, Conflict }

public sealed record CatalogAdminResult<T>(CatalogAdminStatus Status, T? Value = default, string? Message = null)
{
    public static CatalogAdminResult<T> Ok(T value) => new(CatalogAdminStatus.Succeeded, value);
    public static CatalogAdminResult<T> Fail(CatalogAdminStatus status, string message) => new(status, default, message);
}

public interface ICatalogAdminService
{
    Task<CatalogAdminResult<AdminProductPage>> ListAsync(string? search, int page, int pageSize, CancellationToken cancellationToken = default);
    Task<CatalogAdminResult<AdminProductResponse>> GetAsync(Guid productId, CancellationToken cancellationToken = default);
    Task<CatalogAdminResult<AdminProductResponse>> CreateAsync(CreateProductRequest request, CancellationToken cancellationToken = default);
    Task<CatalogAdminResult<AdminProductResponse>> UpdateAsync(Guid productId, UpdateProductRequest request, CancellationToken cancellationToken = default);
    Task<CatalogAdminResult<AdminProductResponse>> SaveTranslationAsync(Guid productId, string locale, SaveTranslationRequest request, CancellationToken cancellationToken = default);
    Task<CatalogAdminResult<AdminProductResponse>> DeleteTranslationAsync(Guid productId, string locale, CancellationToken cancellationToken = default);
    Task<CatalogAdminResult<AdminProductResponse>> AddVariantAsync(Guid productId, SaveVariantRequest request, CancellationToken cancellationToken = default);
    Task<CatalogAdminResult<AdminProductResponse>> UpdateVariantAsync(Guid productId, Guid variantId, SaveVariantRequest request, CancellationToken cancellationToken = default);
    Task<CatalogAdminResult<AdminProductResponse>> MakeDefaultAsync(Guid productId, Guid variantId, CancellationToken cancellationToken = default);
    Task<CatalogAdminResult<AdminProductResponse>> SetImageAsync(Guid productId, Stream content, long length, CancellationToken cancellationToken = default);
    Task<CatalogAdminResult<AdminProductResponse>> RemoveImageAsync(Guid productId, CancellationToken cancellationToken = default);
}

public interface IStockService
{
    /// <summary>Every active variant with this store's stock (Carried false = no stock row). Store staff always see their own store; an administrator names one.</summary>
    Task<CatalogAdminResult<IReadOnlyList<StockRowResponse>>> ListAsync(Guid? storeId, CancellationToken cancellationToken = default);
    Task<CatalogAdminResult<StockRowResponse>> SetAsync(Guid? storeId, Guid variantId, SetStockRequest request, CancellationToken cancellationToken = default);
}

public sealed record ProductBundle(Product Product, string CategoryName, IReadOnlyList<ProductVariant> Variants, IReadOnlyList<ProductTranslation> Translations);

public enum CatalogChange
{
    Succeeded,
    NotFound,
    DuplicateSku,
    CategoryMissing,
    VariantInactive,
    CannotDeactivateDefault,
    Conflict
}

/// <summary>Where uploaded product photos are kept. The local-folder version is the first; cloud storage can replace it without touching callers.</summary>
public interface IImageStore
{
    /// <summary>Saves the bytes and returns the public path (for example /media/products/abc.jpg).</summary>
    Task<string> SaveAsync(string name, string extension, byte[] content, CancellationToken cancellationToken = default);

    /// <summary>Saves into another folder of the media area (for example "offers") and returns the public path.</summary>
    Task<string> SaveAsync(string folder, string name, string extension, byte[] content, CancellationToken cancellationToken = default);

    /// <summary>Removes a file this store saved. Anything else (an external link) is ignored.</summary>
    Task DeleteAsync(string? publicPath, CancellationToken cancellationToken = default);
}

public interface ICatalogAdminStore
{
    Task<(IReadOnlyList<ProductBundle> Items, int Total)> ListProductsAsync(string? search, int skip, int take, CancellationToken cancellationToken = default);
    Task<ProductBundle?> GetProductAsync(Guid productId, CancellationToken cancellationToken = default);
    Task<CatalogChange> CreateProductAsync(Product product, ProductVariant firstVariant, CancellationToken cancellationToken = default);
    Task<CatalogChange> UpdateProductAsync(Guid productId, string name, string description, Guid categoryId, string unitOfMeasure, bool isActive, CancellationToken cancellationToken = default);
    Task<CatalogChange> SaveTranslationAsync(Guid productId, string locale, string name, string? description, CancellationToken cancellationToken = default);
    Task<CatalogChange> DeleteTranslationAsync(Guid productId, string locale, CancellationToken cancellationToken = default);
    Task<CatalogChange> AddVariantAsync(Guid productId, ProductVariant variant, CancellationToken cancellationToken = default);
    Task<CatalogChange> UpdateVariantAsync(Guid productId, Guid variantId, string sku, string label, decimal price, decimal? mrp, int sortOrder, bool isActive, CancellationToken cancellationToken = default);
    Task<CatalogChange> MakeDefaultAsync(Guid productId, Guid variantId, CancellationToken cancellationToken = default);

    /// <summary>Sets (or clears) the picture and returns the previous path so its file can be removed.</summary>
    Task<(CatalogChange Change, string? Previous)> SetImageAsync(Guid productId, string? publicPath, CancellationToken cancellationToken = default);

    Task<Store?> GetStoreForStockAsync(Guid storeId, Guid organizationId, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<StockRowResponse>> ListStockAsync(Guid storeId, CancellationToken cancellationToken = default);
    Task<StockRowResponse?> SetStockAsync(Guid storeId, Guid variantId, bool carried, int available, int reorderThreshold, CancellationToken cancellationToken = default);
}
