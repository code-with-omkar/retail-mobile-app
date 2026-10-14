using Microsoft.EntityFrameworkCore;
using QuickCommerce.Application.Interfaces;
using QuickCommerce.Application.Services;
using QuickCommerce.Domain;

namespace QuickCommerce.Infrastructure.Persistence;

/// <summary>Catalogue editing and per-store stock on SQL Server. The database also refuses duplicate SKUs and a second default size, so a race cannot slip past the checks here.</summary>
public sealed class EfCatalogAdminStore(QuickCommerceDbContext db) : ICatalogAdminStore
{
    private const int Attempts = 4;

    public async Task<(IReadOnlyList<ProductBundle> Items, int Total)> ListProductsAsync(string? search, int skip, int take, CancellationToken cancellationToken = default)
    {
        var query = db.Products.AsNoTracking().AsQueryable();
        if (search is not null)
        {
            query = query.Where(product => product.Name.Contains(search) || product.Sku.Contains(search));
        }

        var total = await query.CountAsync(cancellationToken);
        var products = await query.OrderBy(product => product.Name).ThenBy(product => product.Id).Skip(skip).Take(take).ToListAsync(cancellationToken);
        return (await BundleAsync(products, cancellationToken), total);
    }

    public async Task<ProductBundle?> GetProductAsync(Guid productId, CancellationToken cancellationToken = default)
    {
        var product = await db.Products.AsNoTracking().FirstOrDefaultAsync(item => item.Id == productId, cancellationToken);
        return product is null ? null : (await BundleAsync([product], cancellationToken)).Single();
    }

    private async Task<IReadOnlyList<ProductBundle>> BundleAsync(List<Product> products, CancellationToken cancellationToken)
    {
        var ids = products.Select(product => product.Id).ToArray();
        var variants = await db.ProductVariants.AsNoTracking().Where(variant => ids.Contains(variant.ProductId)).ToListAsync(cancellationToken);
        var translations = await db.ProductTranslations.AsNoTracking().Where(translation => ids.Contains(translation.ProductId)).ToListAsync(cancellationToken);
        var categoryIds = products.Select(product => product.CategoryId).Distinct().ToArray();
        var categories = await db.Categories.AsNoTracking().Where(category => categoryIds.Contains(category.Id)).ToDictionaryAsync(category => category.Id, category => category.Name, cancellationToken);
        return products.Select(product => new ProductBundle(
            product,
            categories.GetValueOrDefault(product.CategoryId, string.Empty),
            variants.Where(variant => variant.ProductId == product.Id).ToArray(),
            translations.Where(translation => translation.ProductId == product.Id).ToArray())).ToArray();
    }

    private async Task<bool> SkuTakenAsync(string sku, Guid? exceptVariantId, Guid? exceptProductId, CancellationToken cancellationToken) =>
        await db.ProductVariants.AnyAsync(variant => variant.Sku == sku && variant.Id != exceptVariantId, cancellationToken) ||
        await db.Products.AnyAsync(product => product.Sku == sku && product.Id != exceptProductId, cancellationToken);

    private static bool IsDuplicate(DbUpdateException exception) => exception.InnerException?.Message.Contains("duplicate", StringComparison.OrdinalIgnoreCase) == true;

    public async Task<CatalogChange> CreateProductAsync(Product product, ProductVariant firstVariant, CancellationToken cancellationToken = default)
    {
        if (!await db.Categories.AnyAsync(category => category.Id == product.CategoryId && category.IsActive, cancellationToken))
        {
            return CatalogChange.CategoryMissing;
        }

        if (await SkuTakenAsync(product.Sku, null, null, cancellationToken))
        {
            return CatalogChange.DuplicateSku;
        }

        db.Products.Add(product);
        await db.SaveChangesAsync(cancellationToken);
        db.ProductVariants.Add(firstVariant);
        try
        {
            await db.SaveChangesAsync(cancellationToken);
            return CatalogChange.Succeeded;
        }
        catch (DbUpdateException exception)
        {
            db.ChangeTracker.Clear();
            await db.Products.Where(item => item.Id == product.Id).ExecuteDeleteAsync(cancellationToken);
            return IsDuplicate(exception) ? CatalogChange.DuplicateSku : throw exception;
        }
    }

    public async Task<CatalogChange> UpdateProductAsync(Guid productId, string name, string description, Guid categoryId, string unitOfMeasure, bool isActive, CancellationToken cancellationToken = default)
    {
        var product = await db.Products.FirstOrDefaultAsync(item => item.Id == productId, cancellationToken);
        if (product is null)
        {
            return CatalogChange.NotFound;
        }

        if (!await db.Categories.AnyAsync(category => category.Id == categoryId && (category.IsActive || category.Id == product.CategoryId), cancellationToken))
        {
            return CatalogChange.CategoryMissing;
        }

        product.Name = name;
        product.Description = description;
        product.CategoryId = categoryId;
        product.UnitOfMeasure = unitOfMeasure;
        product.IsActive = isActive;
        await db.SaveChangesAsync(cancellationToken);
        db.ChangeTracker.Clear();
        return CatalogChange.Succeeded;
    }

    public async Task<CatalogChange> SaveTranslationAsync(Guid productId, string locale, string name, string? description, CancellationToken cancellationToken = default)
    {
        if (!await db.Products.AnyAsync(item => item.Id == productId, cancellationToken))
        {
            return CatalogChange.NotFound;
        }

        var existing = await db.ProductTranslations.FirstOrDefaultAsync(item => item.ProductId == productId && item.Locale == locale, cancellationToken);
        if (existing is null)
        {
            db.ProductTranslations.Add(new ProductTranslation { ProductId = productId, Locale = locale, Name = name, Description = description });
        }
        else
        {
            existing.Name = name;
            existing.Description = description;
        }

        await db.SaveChangesAsync(cancellationToken);
        db.ChangeTracker.Clear();
        return CatalogChange.Succeeded;
    }

    public async Task<CatalogChange> DeleteTranslationAsync(Guid productId, string locale, CancellationToken cancellationToken = default)
    {
        if (!await db.Products.AnyAsync(item => item.Id == productId, cancellationToken))
        {
            return CatalogChange.NotFound;
        }

        await db.ProductTranslations.Where(item => item.ProductId == productId && item.Locale == locale).ExecuteDeleteAsync(cancellationToken);
        return CatalogChange.Succeeded;
    }

    public async Task<CatalogChange> AddVariantAsync(Guid productId, ProductVariant variant, CancellationToken cancellationToken = default)
    {
        if (!await db.Products.AnyAsync(item => item.Id == productId, cancellationToken))
        {
            return CatalogChange.NotFound;
        }

        if (await SkuTakenAsync(variant.Sku, null, null, cancellationToken))
        {
            return CatalogChange.DuplicateSku;
        }

        variant.IsDefault = false;
        db.ProductVariants.Add(variant);
        try
        {
            await db.SaveChangesAsync(cancellationToken);
            return CatalogChange.Succeeded;
        }
        catch (DbUpdateException exception) when (IsDuplicate(exception))
        {
            db.ChangeTracker.Clear();
            return CatalogChange.DuplicateSku;
        }
    }

    public async Task<CatalogChange> UpdateVariantAsync(Guid productId, Guid variantId, string sku, string label, decimal price, decimal? mrp, int sortOrder, bool isActive, CancellationToken cancellationToken = default)
    {
        var product = await db.Products.FirstOrDefaultAsync(item => item.Id == productId, cancellationToken);
        var variants = product is null ? [] : await db.ProductVariants.Where(item => item.ProductId == productId).ToListAsync(cancellationToken);
        var variant = variants.FirstOrDefault(item => item.Id == variantId);
        if (product is null || variant is null)
        {
            return CatalogChange.NotFound;
        }

        if (!string.Equals(variant.Sku, sku, StringComparison.Ordinal) && await SkuTakenAsync(sku, variantId, productId, cancellationToken))
        {
            return CatalogChange.DuplicateSku;
        }

        var change = CatalogRules.UpdateVariant(product, variants, variant, sku, label, price, mrp, sortOrder, isActive);
        if (change != CatalogChange.Succeeded)
        {
            return change;
        }

        try
        {
            await db.SaveChangesAsync(cancellationToken);
            return CatalogChange.Succeeded;
        }
        catch (DbUpdateException exception) when (IsDuplicate(exception))
        {
            return CatalogChange.DuplicateSku;
        }
        finally
        {
            db.ChangeTracker.Clear();
        }
    }

    public async Task<CatalogChange> MakeDefaultAsync(Guid productId, Guid variantId, CancellationToken cancellationToken = default)
    {
        var product = await db.Products.FirstOrDefaultAsync(item => item.Id == productId, cancellationToken);
        var variants = product is null ? [] : await db.ProductVariants.Where(item => item.ProductId == productId).ToListAsync(cancellationToken);
        var target = variants.FirstOrDefault(item => item.Id == variantId);
        if (product is null || target is null)
        {
            return CatalogChange.NotFound;
        }

        if (!target.IsActive)
        {
            return CatalogChange.VariantInactive;
        }

        if (target.IsDefault)
        {
            return CatalogChange.Succeeded;
        }

        // Two steps in one transaction: the database allows only one default per product at any moment.
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        foreach (var current in variants.Where(item => item.IsDefault))
        {
            current.IsDefault = false;
        }

        await db.SaveChangesAsync(cancellationToken);
        target.IsDefault = true;
        CatalogRules.SyncFromDefault(product, variants);
        await db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        db.ChangeTracker.Clear();
        return CatalogChange.Succeeded;
    }

    public async Task<(CatalogChange Change, string? Previous)> SetImageAsync(Guid productId, string? publicPath, CancellationToken cancellationToken = default)
    {
        var product = await db.Products.FirstOrDefaultAsync(item => item.Id == productId, cancellationToken);
        if (product is null)
        {
            return (CatalogChange.NotFound, null);
        }

        var previous = product.ImageUrl;
        product.ImageUrl = publicPath;
        await db.SaveChangesAsync(cancellationToken);
        db.ChangeTracker.Clear();
        return (CatalogChange.Succeeded, previous);
    }

    public Task<Store?> GetStoreForStockAsync(Guid storeId, Guid organizationId, CancellationToken cancellationToken = default) =>
        db.Stores.AsNoTracking().FirstOrDefaultAsync(store => store.Id == storeId && store.OrganizationId == organizationId && store.IsActive, cancellationToken);

    public async Task<IReadOnlyList<StockRowResponse>> ListStockAsync(Guid storeId, CancellationToken cancellationToken = default)
    {
        var rows = await db.ProductVariants.AsNoTracking()
            .Where(variant => variant.IsActive)
            .Join(db.Products.AsNoTracking().Where(product => product.IsActive), variant => variant.ProductId, product => product.Id, (variant, product) => new { variant, product })
            .GroupJoin(db.StoreVariantInventory.AsNoTracking().Where(stock => stock.StoreId == storeId), row => row.variant.Id, stock => stock.VariantId, (row, stock) => new { row.variant, row.product, stock })
            .SelectMany(row => row.stock.DefaultIfEmpty(), (row, stock) => new { row.variant, row.product, stock })
            .OrderBy(row => row.product.Name).ThenBy(row => row.variant.SortOrder)
            .ToListAsync(cancellationToken);
        return rows.Select(row => new StockRowResponse(row.product.Id, row.product.Name, row.variant.Id, row.variant.Label, row.stock is not null, row.stock?.AvailableQuantity ?? 0, row.stock?.ReorderThreshold ?? 5)).ToArray();
    }

    public async Task<StockRowResponse?> SetStockAsync(Guid storeId, Guid variantId, bool carried, int available, int reorderThreshold, CancellationToken cancellationToken = default)
    {
        for (var attempt = 1; ; attempt++)
        {
            var variant = await db.ProductVariants.AsNoTracking().FirstOrDefaultAsync(item => item.Id == variantId, cancellationToken);
            var product = variant is null ? null : await db.Products.AsNoTracking().FirstOrDefaultAsync(item => item.Id == variant.ProductId, cancellationToken);
            if (variant is null || product is null)
            {
                return null;
            }

            try
            {
                var row = await db.StoreVariantInventory.FirstOrDefaultAsync(item => item.StoreId == storeId && item.VariantId == variantId, cancellationToken);
                if (!carried)
                {
                    if (row is not null)
                    {
                        db.StoreVariantInventory.Remove(row);
                    }
                }
                else if (row is null)
                {
                    db.StoreVariantInventory.Add(new StoreVariantInventory { StoreId = storeId, VariantId = variantId, AvailableQuantity = available, ReorderThreshold = reorderThreshold });
                }
                else
                {
                    row.AvailableQuantity = available;
                    row.ReorderThreshold = reorderThreshold;
                }

                await db.SaveChangesAsync(cancellationToken);
                db.ChangeTracker.Clear();
                return new StockRowResponse(product.Id, product.Name, variant.Id, variant.Label, carried, carried ? available : 0, carried ? reorderThreshold : 5);
            }
            catch (Exception ex) when ((ex is DbUpdateConcurrencyException || ex is DbUpdateException || SqlErrors.IsDeadlock(ex)) && attempt < Attempts)
            {
                // An order took stock at the same moment: ask again against the new row.
                db.ChangeTracker.Clear();
            }
        }
    }
}
