using System.Security.Cryptography;
using System.Text.RegularExpressions;
using QuickCommerce.Application.Caching;
using QuickCommerce.Application.DTOs;
using QuickCommerce.Application.Interfaces;
using QuickCommerce.Domain;

namespace QuickCommerce.Application.Services;

/// <summary>Rules for a product and its variants that the SQL and in-memory stores both apply, so they behave the same.</summary>
public static class CatalogRules
{
    /// <summary>The product's own price and MRP follow its default variant (older readers still use them).</summary>
    public static void SyncFromDefault(Product product, IEnumerable<ProductVariant> variants)
    {
        var defaultVariant = variants.FirstOrDefault(variant => variant.IsDefault);
        if (defaultVariant is not null)
        {
            product.Price = defaultVariant.Price;
            product.Mrp = defaultVariant.Mrp;
        }
    }

    public static CatalogChange UpdateVariant(Product product, IReadOnlyCollection<ProductVariant> variants, ProductVariant variant, string sku, string label, decimal price, decimal? mrp, int sortOrder, bool isActive)
    {
        if (!isActive && variant.IsDefault)
        {
            return CatalogChange.CannotDeactivateDefault;
        }

        variant.Sku = sku;
        variant.Label = label;
        variant.Price = price;
        variant.Mrp = mrp;
        variant.SortOrder = sortOrder;
        variant.IsActive = isActive;
        SyncFromDefault(product, variants);
        return CatalogChange.Succeeded;
    }
}

public sealed partial class CatalogAdminService(
    ICatalogAdminStore store,
    ICurrentUserContextResolver contextResolver,
    IImageStore images,
    ICacheService? cache = null) : ICatalogAdminService
{
    public const int MaxImageBytes = 2 * 1024 * 1024;
    public const decimal MaxPrice = 100_000m;
    private static readonly string[] Locales = ["mr"];

    [GeneratedRegex("^[A-Za-z0-9][A-Za-z0-9._-]*$")]
    private static partial Regex SkuPattern();

    private static CatalogAdminResult<T> Forbidden<T>() => CatalogAdminResult<T>.Fail(CatalogAdminStatus.Forbidden, "Only an administrator can change the catalogue.");
    private static CatalogAdminResult<T> Invalid<T>(string message) => CatalogAdminResult<T>.Fail(CatalogAdminStatus.Invalid, message);

    private async Task<bool> IsAdminAsync(CancellationToken cancellationToken)
    {
        var context = await contextResolver.ResolveAsync(cancellationToken);
        return context is { Role: Role.Admin or Role.ApplicationAdmin };
    }

    public async Task<CatalogAdminResult<AdminProductPage>> ListAsync(string? search, int page, int pageSize, CancellationToken cancellationToken = default)
    {
        if (!await IsAdminAsync(cancellationToken))
        {
            return Forbidden<AdminProductPage>();
        }

        page = Math.Max(1, page);
        pageSize = Math.Clamp(pageSize, 1, 100);
        var term = string.IsNullOrWhiteSpace(search) ? null : search.Trim();
        var (items, total) = await store.ListProductsAsync(term, (page - 1) * pageSize, pageSize, cancellationToken);
        return CatalogAdminResult<AdminProductPage>.Ok(new AdminProductPage(items.Select(Map).ToArray(), page, pageSize, total));
    }

    public async Task<CatalogAdminResult<AdminProductResponse>> GetAsync(Guid productId, CancellationToken cancellationToken = default)
    {
        if (!await IsAdminAsync(cancellationToken))
        {
            return Forbidden<AdminProductResponse>();
        }

        return await ReloadAsync(productId, cancellationToken);
    }

    public async Task<CatalogAdminResult<AdminProductResponse>> CreateAsync(CreateProductRequest request, CancellationToken cancellationToken = default)
    {
        if (!await IsAdminAsync(cancellationToken))
        {
            return Forbidden<AdminProductResponse>();
        }

        var sku = request.Sku?.Trim() ?? string.Empty;
        var unit = request.UnitOfMeasure?.Trim() ?? string.Empty;
        var problem = CheckSku(sku, 64) ?? CheckText(request.Name, "Name", 160) ?? CheckLength(request.Description, "Description", 2000) ?? CheckText(unit, "Unit", 32) ?? CheckPrice(request.Price, request.Mrp);
        if (problem is not null)
        {
            return Invalid<AdminProductResponse>(problem);
        }

        var product = new Product { Sku = sku, Name = request.Name.Trim(), Description = (request.Description ?? string.Empty).Trim(), Price = request.Price, Mrp = request.Mrp, UnitOfMeasure = unit, CategoryId = request.CategoryId };
        var first = new ProductVariant { ProductId = product.Id, Sku = sku, Label = unit, Price = request.Price, Mrp = request.Mrp, IsDefault = true };
        var change = await store.CreateProductAsync(product, first, cancellationToken);
        return change == CatalogChange.Succeeded ? await ReloadAsync(product.Id, cancellationToken) : Failure(change);
    }

    public async Task<CatalogAdminResult<AdminProductResponse>> UpdateAsync(Guid productId, UpdateProductRequest request, CancellationToken cancellationToken = default)
    {
        if (!await IsAdminAsync(cancellationToken))
        {
            return Forbidden<AdminProductResponse>();
        }

        var unit = request.UnitOfMeasure?.Trim() ?? string.Empty;
        var problem = CheckText(request.Name, "Name", 160) ?? CheckLength(request.Description, "Description", 2000) ?? CheckText(unit, "Unit", 32);
        if (problem is not null)
        {
            return Invalid<AdminProductResponse>(problem);
        }

        var change = await store.UpdateProductAsync(productId, request.Name.Trim(), (request.Description ?? string.Empty).Trim(), request.CategoryId, unit, request.IsActive, cancellationToken);
        return change == CatalogChange.Succeeded ? await ReloadAsync(productId, cancellationToken) : Failure(change);
    }

    public async Task<CatalogAdminResult<AdminProductResponse>> SaveTranslationAsync(Guid productId, string locale, SaveTranslationRequest request, CancellationToken cancellationToken = default)
    {
        if (!await IsAdminAsync(cancellationToken))
        {
            return Forbidden<AdminProductResponse>();
        }

        locale = (locale ?? string.Empty).Trim().ToLowerInvariant();
        var problem = !Locales.Contains(locale) ? "That language is not supported." : CheckText(request.Name, "Name", 160) ?? CheckLength(request.Description, "Description", 2000);
        if (problem is not null)
        {
            return Invalid<AdminProductResponse>(problem);
        }

        var change = await store.SaveTranslationAsync(productId, locale, request.Name.Trim(), string.IsNullOrWhiteSpace(request.Description) ? null : request.Description.Trim(), cancellationToken);
        return change == CatalogChange.Succeeded ? await ReloadAsync(productId, cancellationToken) : Failure(change);
    }

    public async Task<CatalogAdminResult<AdminProductResponse>> DeleteTranslationAsync(Guid productId, string locale, CancellationToken cancellationToken = default)
    {
        if (!await IsAdminAsync(cancellationToken))
        {
            return Forbidden<AdminProductResponse>();
        }

        var change = await store.DeleteTranslationAsync(productId, (locale ?? string.Empty).Trim().ToLowerInvariant(), cancellationToken);
        return change == CatalogChange.Succeeded ? await ReloadAsync(productId, cancellationToken) : Failure(change);
    }

    public async Task<CatalogAdminResult<AdminProductResponse>> AddVariantAsync(Guid productId, SaveVariantRequest request, CancellationToken cancellationToken = default)
    {
        if (!await IsAdminAsync(cancellationToken))
        {
            return Forbidden<AdminProductResponse>();
        }

        var sku = request.Sku?.Trim() ?? string.Empty;
        var problem = CheckVariant(request, sku);
        if (problem is not null)
        {
            return Invalid<AdminProductResponse>(problem);
        }

        var change = await store.AddVariantAsync(productId, new ProductVariant { ProductId = productId, Sku = sku, Label = request.Label.Trim(), Price = request.Price, Mrp = request.Mrp, SortOrder = request.SortOrder, IsActive = request.IsActive }, cancellationToken);
        return change == CatalogChange.Succeeded ? await ReloadAsync(productId, cancellationToken) : Failure(change);
    }

    public async Task<CatalogAdminResult<AdminProductResponse>> UpdateVariantAsync(Guid productId, Guid variantId, SaveVariantRequest request, CancellationToken cancellationToken = default)
    {
        if (!await IsAdminAsync(cancellationToken))
        {
            return Forbidden<AdminProductResponse>();
        }

        var sku = request.Sku?.Trim() ?? string.Empty;
        var problem = CheckVariant(request, sku);
        if (problem is not null)
        {
            return Invalid<AdminProductResponse>(problem);
        }

        var change = await store.UpdateVariantAsync(productId, variantId, sku, request.Label.Trim(), request.Price, request.Mrp, request.SortOrder, request.IsActive, cancellationToken);
        return change == CatalogChange.Succeeded ? await ReloadAsync(productId, cancellationToken) : Failure(change);
    }

    public async Task<CatalogAdminResult<AdminProductResponse>> MakeDefaultAsync(Guid productId, Guid variantId, CancellationToken cancellationToken = default)
    {
        if (!await IsAdminAsync(cancellationToken))
        {
            return Forbidden<AdminProductResponse>();
        }

        var change = await store.MakeDefaultAsync(productId, variantId, cancellationToken);
        return change == CatalogChange.Succeeded ? await ReloadAsync(productId, cancellationToken) : Failure(change);
    }

    public async Task<CatalogAdminResult<AdminProductResponse>> SetImageAsync(Guid productId, Stream content, long length, CancellationToken cancellationToken = default)
    {
        if (!await IsAdminAsync(cancellationToken))
        {
            return Forbidden<AdminProductResponse>();
        }

        if (length <= 0 || length > MaxImageBytes)
        {
            return Invalid<AdminProductResponse>("The picture must be 2 MB or smaller.");
        }

        using var buffer = new MemoryStream();
        await content.CopyToAsync(buffer, cancellationToken);
        var bytes = buffer.ToArray();
        var extension = ImageType(bytes);
        if (bytes.Length > MaxImageBytes || extension is null)
        {
            return Invalid<AdminProductResponse>(bytes.Length > MaxImageBytes ? "The picture must be 2 MB or smaller." : "Use a JPEG, PNG or WebP picture.");
        }

        if (await store.GetProductAsync(productId, cancellationToken) is null)
        {
            return CatalogAdminResult<AdminProductResponse>.Fail(CatalogAdminStatus.NotFound, "Product not found.");
        }

        // The name is made here, never taken from the upload, so a file can never land outside the media folder or overwrite another.
        var path = await images.SaveAsync($"{productId:N}-{Convert.ToHexString(RandomNumberGenerator.GetBytes(4)).ToLowerInvariant()}", extension, bytes, cancellationToken);
        var (change, previous) = await store.SetImageAsync(productId, path, cancellationToken);
        if (change != CatalogChange.Succeeded)
        {
            await images.DeleteAsync(path, cancellationToken);
            return Failure(change);
        }

        await images.DeleteAsync(previous, cancellationToken);
        return await ReloadAsync(productId, cancellationToken);
    }

    public async Task<CatalogAdminResult<AdminProductResponse>> RemoveImageAsync(Guid productId, CancellationToken cancellationToken = default)
    {
        if (!await IsAdminAsync(cancellationToken))
        {
            return Forbidden<AdminProductResponse>();
        }

        var (change, previous) = await store.SetImageAsync(productId, null, cancellationToken);
        if (change != CatalogChange.Succeeded)
        {
            return Failure(change);
        }

        await images.DeleteAsync(previous, cancellationToken);
        return await ReloadAsync(productId, cancellationToken);
    }

    /// <summary>Reads the product back (and forgets what customers were shown about it) after every change.</summary>
    private async Task<CatalogAdminResult<AdminProductResponse>> ReloadAsync(Guid productId, CancellationToken cancellationToken)
    {
        var bundle = await store.GetProductAsync(productId, cancellationToken);
        if (bundle is null)
        {
            return CatalogAdminResult<AdminProductResponse>.Fail(CatalogAdminStatus.NotFound, "Product not found.");
        }

        if (cache is not null)
        {
            await cache.RemoveAsync(CatalogCacheKeys.Product(productId), cancellationToken);
            await cache.RemoveAsync(CatalogCacheKeys.CatalogProduct(productId), cancellationToken);
        }

        return CatalogAdminResult<AdminProductResponse>.Ok(Map(bundle));
    }

    private static CatalogAdminResult<AdminProductResponse> Failure(CatalogChange change) => change switch
    {
        CatalogChange.NotFound => CatalogAdminResult<AdminProductResponse>.Fail(CatalogAdminStatus.NotFound, "Not found."),
        CatalogChange.DuplicateSku => CatalogAdminResult<AdminProductResponse>.Fail(CatalogAdminStatus.Conflict, "That SKU is already used."),
        CatalogChange.CategoryMissing => Invalid<AdminProductResponse>("Choose a category."),
        CatalogChange.VariantInactive => Invalid<AdminProductResponse>("Only an active size can be the default."),
        CatalogChange.CannotDeactivateDefault => Invalid<AdminProductResponse>("Make another size the default before switching this one off."),
        _ => CatalogAdminResult<AdminProductResponse>.Fail(CatalogAdminStatus.Conflict, "The product changed at the same time. Try again.")
    };

    private static string? CheckVariant(SaveVariantRequest request, string sku) =>
        CheckSku(sku, 80) ?? CheckText(request.Label, "Size", 40) ?? CheckPrice(request.Price, request.Mrp) ?? (request.SortOrder is < 0 or > 1000 ? "Order must be between 0 and 1000." : null);

    private static string? CheckSku(string sku, int max) =>
        sku.Length is 0 || sku.Length > max || !SkuPattern().IsMatch(sku) ? $"SKU must be letters, digits, dot, dash or underscore, up to {max} characters." : null;

    private static string? CheckText(string? value, string name, int max) =>
        string.IsNullOrWhiteSpace(value) ? $"{name} is required." : value.Trim().Length > max ? $"{name} can be up to {max} characters." : null;

    private static string? CheckLength(string? value, string name, int max) => value is not null && value.Trim().Length > max ? $"{name} can be up to {max} characters." : null;

    private static string? CheckPrice(decimal price, decimal? mrp) =>
        price <= 0 || price > MaxPrice || decimal.Round(price, 2) != price ? $"Price must be above 0 and at most {MaxPrice:0}, with at most 2 decimals."
        : mrp is { } m && (m < price || m > MaxPrice || decimal.Round(m, 2) != m) ? "MRP cannot be below the price (and has at most 2 decimals)."
        : null;

    /// <summary>The type of a picture by its first bytes, whatever the upload claims to be. Null = not an accepted picture.</summary>
    public static string? ImageType(byte[] b)
    {
        if (b.Length >= 3 && b[0] == 0xFF && b[1] == 0xD8 && b[2] == 0xFF) return "jpg";
        if (b.Length >= 8 && b[0] == 0x89 && b[1] == 0x50 && b[2] == 0x4E && b[3] == 0x47 && b[4] == 0x0D && b[5] == 0x0A && b[6] == 0x1A && b[7] == 0x0A) return "png";
        if (b.Length >= 12 && b[0] == 'R' && b[1] == 'I' && b[2] == 'F' && b[3] == 'F' && b[8] == 'W' && b[9] == 'E' && b[10] == 'B' && b[11] == 'P') return "webp";
        return null;
    }

    private static AdminProductResponse Map(ProductBundle bundle)
    {
        var product = bundle.Product;
        return new AdminProductResponse(
            product.Id, product.Sku, product.Name, product.Description, product.CategoryId, bundle.CategoryName, product.UnitOfMeasure, product.ImageUrl, product.IsActive, product.Price, product.Mrp,
            bundle.Variants.OrderBy(variant => variant.SortOrder).ThenBy(variant => variant.Price).Select(variant => new AdminVariantResponse(variant.Id, variant.Sku, variant.Label, variant.Price, variant.Mrp, variant.SortOrder, variant.IsDefault, variant.IsActive)).ToArray(),
            bundle.Translations.OrderBy(translation => translation.Locale).Select(translation => new AdminTranslationResponse(translation.Locale, translation.Name, translation.Description)).ToArray());
    }
}

public sealed class StockService(ICatalogAdminStore store, ICurrentUserContextResolver contextResolver) : IStockService
{
    public const int MaxQuantity = 100_000;

    /// <summary>Staff always use their own store; an administrator names one. Null = not allowed.</summary>
    private async Task<(Guid? StoreId, CatalogAdminStatus Status, string? Message)> ScopeAsync(Guid? requested, CancellationToken cancellationToken)
    {
        var context = await contextResolver.ResolveAsync(cancellationToken);
        if (context is null || context.Role is not (Role.StoreStaff or Role.Admin or Role.ApplicationAdmin))
        {
            return (null, CatalogAdminStatus.Forbidden, "Only store staff or an administrator can manage stock.");
        }

        var storeId = context.Role == Role.StoreStaff ? context.StoreId : requested;
        if (storeId is null)
        {
            return (null, context.Role == Role.StoreStaff ? CatalogAdminStatus.Forbidden : CatalogAdminStatus.Invalid, context.Role == Role.StoreStaff ? "Store staff must be assigned to a store." : "Choose a store.");
        }

        return await store.GetStoreForStockAsync(storeId.Value, context.OrganizationId, cancellationToken) is null
            ? (null, CatalogAdminStatus.NotFound, "Store not found.")
            : (storeId, CatalogAdminStatus.Succeeded, null);
    }

    public async Task<CatalogAdminResult<IReadOnlyList<StockRowResponse>>> ListAsync(Guid? storeId, CancellationToken cancellationToken = default)
    {
        var scope = await ScopeAsync(storeId, cancellationToken);
        return scope.StoreId is null
            ? CatalogAdminResult<IReadOnlyList<StockRowResponse>>.Fail(scope.Status, scope.Message!)
            : CatalogAdminResult<IReadOnlyList<StockRowResponse>>.Ok(await store.ListStockAsync(scope.StoreId.Value, cancellationToken));
    }

    public async Task<CatalogAdminResult<StockRowResponse>> SetAsync(Guid? storeId, Guid variantId, SetStockRequest request, CancellationToken cancellationToken = default)
    {
        var scope = await ScopeAsync(storeId, cancellationToken);
        if (scope.StoreId is null)
        {
            return CatalogAdminResult<StockRowResponse>.Fail(scope.Status, scope.Message!);
        }

        if (request.Available is < 0 or > MaxQuantity || request.ReorderThreshold is < 0 or > MaxQuantity)
        {
            return CatalogAdminResult<StockRowResponse>.Fail(CatalogAdminStatus.Invalid, $"Stock must be between 0 and {MaxQuantity}.");
        }

        if (request.PriceOverride is { } own && (own <= 0 || own > CatalogAdminService.MaxPrice || decimal.Round(own, 2) != own))
        {
            return CatalogAdminResult<StockRowResponse>.Fail(CatalogAdminStatus.Invalid, "The store price must be above 0, up to ₹1,00,000, with at most 2 decimals.");
        }

        var row = await store.SetStockAsync(scope.StoreId.Value, variantId, request.Carried, request.Available, request.ReorderThreshold, request.Carried ? request.PriceOverride : null, cancellationToken);
        return row is null
            ? CatalogAdminResult<StockRowResponse>.Fail(CatalogAdminStatus.NotFound, "That product size was not found.")
            : CatalogAdminResult<StockRowResponse>.Ok(row);
    }
}
