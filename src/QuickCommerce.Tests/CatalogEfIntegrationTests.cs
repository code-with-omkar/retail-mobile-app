using Microsoft.EntityFrameworkCore;
using QuickCommerce.Application;
using QuickCommerce.Application.DTOs;
using QuickCommerce.Application.Services;
using QuickCommerce.Domain;
using QuickCommerce.Infrastructure.Caching;
using QuickCommerce.Infrastructure.Persistence;
using Xunit;

namespace QuickCommerce.Tests;

/// <summary>
/// Runs the catalog queries against a real SQL Server, because the in-memory store cannot prove that EF Core translates
/// them (paging, search through translations, stock lookups, the MRP check constraint).
///
/// Opt-in, like the existing SQL smoke test: set QUICKCOMMERCE_TEST_CONNECTION_STRING to a connection string for a
/// DISPOSABLE database. The test applies migrations to it and adds uniquely named rows, which it removes again.
/// Never point it at a database you care about.
/// </summary>
public sealed class CatalogEfIntegrationTests
{
    [Fact]
    public async Task Catalog_queries_work_against_a_real_sql_server()
    {
        var connectionString = Environment.GetEnvironmentVariable("QUICKCOMMERCE_TEST_CONNECTION_STRING");
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            return;
        }

        var options = new DbContextOptionsBuilder<QuickCommerceDbContext>().UseSqlServer(connectionString).Options;
        await using var db = new QuickCommerceDbContext(options);
        await db.Database.MigrateAsync();

        var token = "ci" + Guid.NewGuid().ToString("N")[..10];
        var marathiToken = "mr" + Guid.NewGuid().ToString("N")[..10];
        var organizationId = (await db.Organizations.AsNoTracking().FirstAsync()).Id;

        var category = new Category { Name = $"Cat {token}" };
        var alpha = new Product { Sku = $"{token}-A", Name = $"{token} Alpha", Description = "first", Price = 40, Mrp = 50, UnitOfMeasure = "1 kg", CategoryId = category.Id };
        var beta = new Product { Sku = $"{token}-B", Name = $"{token} Beta", Description = "second", Price = 30, UnitOfMeasure = "1 kg", CategoryId = category.Id };
        var retired = new Product { Sku = $"{token}-C", Name = $"{token} Gamma", Description = "inactive", Price = 20, UnitOfMeasure = "1 kg", CategoryId = category.Id, IsActive = false };
        var store = new Store { Name = $"Store {token}", Address = "test", Latitude = 40.0, Longitude = 40.0, ServiceRadiusKm = 5, OrganizationId = organizationId };
        var farStore = new Store { Name = $"Far {token}", Address = "test 2", Latitude = 40.01, Longitude = 40.0, ServiceRadiusKm = 5, OrganizationId = organizationId };
        var devanagari = marathiToken + "टोमॅटो";

        db.Categories.Add(category);
        db.Products.AddRange(alpha, beta, retired);
        db.Stores.AddRange(store, farStore);
        await db.SaveChangesAsync();
        db.ProductTranslations.Add(new ProductTranslation { ProductId = alpha.Id, Locale = "mr", Name = devanagari, Description = "ताजे" });
        db.CategoryTranslations.Add(new CategoryTranslation { CategoryId = category.Id, Locale = "mr", Name = "भाज्या " + token });
        // Every product has a default variant; stock and price live on the variant.
        var alphaVariant = new ProductVariant { ProductId = alpha.Id, Sku = alpha.Sku, Label = "1 kg", Price = 40, Mrp = 50, IsDefault = true };
        var betaVariant = new ProductVariant { ProductId = beta.Id, Sku = beta.Sku, Label = "1 kg", Price = 30, IsDefault = true };
        var retiredVariant = new ProductVariant { ProductId = retired.Id, Sku = retired.Sku, Label = "1 kg", Price = 20, IsDefault = true };
        // A second pack size of Beta, which only the first store stocks.
        var betaSmall = new ProductVariant { ProductId = beta.Id, Sku = beta.Sku + "-500", Label = "500 g", Price = 16, Mrp = 17, SortOrder = 1 };
        db.ProductVariants.AddRange(alphaVariant, betaVariant, retiredVariant, betaSmall);
        await db.SaveChangesAsync();
        db.StoreVariantInventory.AddRange(
            new StoreVariantInventory { StoreId = store.Id, VariantId = alphaVariant.Id, AvailableQuantity = 0 },
            new StoreVariantInventory { StoreId = store.Id, VariantId = betaVariant.Id, AvailableQuantity = 3, ReorderThreshold = 5 },
            new StoreVariantInventory { StoreId = store.Id, VariantId = betaSmall.Id, AvailableQuantity = 20, ReorderThreshold = 5 },
            // The second store carries only Beta's default pack, and an inactive product that must not count as carried.
            new StoreVariantInventory { StoreId = farStore.Id, VariantId = betaVariant.Id, AvailableQuantity = 9 },
            new StoreVariantInventory { StoreId = farStore.Id, VariantId = retiredVariant.Id, AvailableQuantity = 9 });
        await db.SaveChangesAsync();

        try
        {
            await using var readDb = new QuickCommerceDbContext(options);
            var data = new EfCommerceStore(readDb);

            // Paging, active filter, stable order, and total count.
            var all = await data.GetActiveProductPageAsync(token, null, 0, 10);
            Assert.Equal(2, all.TotalCount);
            Assert.Equal([alpha.Id, beta.Id], all.Items.Select(item => item.Id).ToArray());
            var second = await data.GetActiveProductPageAsync(token, null, 1, 1);
            Assert.Equal([beta.Id], second.Items.Select(item => item.Id).ToArray());
            Assert.Equal(2, second.TotalCount);

            // Category filter, and search through a translated name only (the English text does not contain the token).
            Assert.Equal(2, (await data.GetActiveProductPageAsync(null, category.Id, 0, 10)).TotalCount);
            Assert.Equal([alpha.Id], (await data.GetActiveProductPageAsync(marathiToken, null, 0, 10)).Items.Select(item => item.Id).ToArray());

            // Translations (Devanagari round trip) and stock rows, each fetched in one query for the whole page.
            var translations = await data.GetProductTranslationsAsync([alpha.Id, beta.Id]);
            Assert.Equal(devanagari, translations.Single().Name);
            var stock = await data.GetStoreVariantInventoryAsync(store.Id, [alphaVariant.Id, betaVariant.Id, Guid.NewGuid()]);
            Assert.Equal([0, 3], stock.OrderBy(row => row.AvailableQuantity).Select(row => row.AvailableQuantity).ToArray());
            Assert.Empty(await data.GetStoreVariantInventoryAsync(store.Id, []));

            // Variants: all of a product's, in sort order, in one query; one by id.
            var betaVariants = await data.GetVariantsAsync([beta.Id]);
            Assert.Equal([betaVariant.Id, betaSmall.Id], betaVariants.Select(item => item.Id).ToArray());
            Assert.Equal("500 g", (await data.GetVariantAsync(betaSmall.Id))!.Label);
            Assert.Null(await data.GetVariantAsync(Guid.NewGuid()));

            // The whole customer flow on top of the real store: MRP, translations, stock flags, nearest store and estimate.
            var service = new CatalogService(data, new StoreSelectionService(), new NoOpCacheService(), new DeliveryEstimator(new DeliverySettings()));
            var page = await service.GetCatalogProductsAsync(new CatalogProductQuery(token, null, 1, 10, store.Id));
            var a = page.Page!.Items.Single(item => item.Id == alpha.Id);
            var b = page.Page.Items.Single(item => item.Id == beta.Id);
            Assert.Equal((50m, 20), (a.Mrp, a.DiscountPercent));
            Assert.Equal(devanagari, a.Translations["mr"].Name);
            Assert.Equal((false, false), (a.InStock, a.LowStock));
            Assert.Equal((true, true), (b.InStock, b.LowStock));
            // Variants in the catalogue: the product's own fields are its default variant, each variant has its own flags.
            Assert.Equal(2, b.Variants!.Count);
            Assert.Equal(("1 kg", 30m, true), (b.UnitOfMeasure, b.Price, b.Variants[0].IsDefault));
            var small = b.Variants.Single(item => item.Label == "500 g");
            Assert.Equal((16m, 17m, 6, false), (small.Price, small.Mrp, small.DiscountPercent, small.IsDefault));
            Assert.Equal((true, false), (small.InStock, small.LowStock));
            Assert.Equal((true, true), (b.Variants[0].InStock, b.Variants[0].LowStock));
            var nearest = await service.FindNearestStoreForCustomerAsync(new NearestStoreQuery(40.0, 40.0));
            Assert.Equal(store.Id, nearest.Store!.Id);
            Assert.Equal(8, nearest.Store.EstimatedMinutes);
            var categories = await service.GetCatalogCategoriesAsync();
            Assert.Equal("भाज्या " + token, categories.Single(item => item.Id == category.Id).Translations["mr"].Name);

            // Store catalogue (carriedOnly): a stock row of any quantity means "carries", inactive products never count.
            var firstStoreCatalogue = await data.GetStoreProductPageAsync(store.Id, token, null, 0, 10);
            Assert.Equal([alpha.Id, beta.Id], firstStoreCatalogue.Items.Select(item => item.Id).ToArray());
            var farCatalogue = await data.GetStoreProductPageAsync(farStore.Id, token, null, 0, 10);
            Assert.Equal([beta.Id], farCatalogue.Items.Select(item => item.Id).ToArray());
            Assert.Equal(1, farCatalogue.TotalCount);
            Assert.Empty((await data.GetStoreProductPageAsync(farStore.Id, marathiToken, null, 0, 10)).Items);
            Assert.Equal([alpha.Id], (await data.GetStoreProductPageAsync(store.Id, marathiToken, null, 0, 10)).Items.Select(item => item.Id).ToArray());
            Assert.Equal([beta.Id], (await data.GetStoreProductPageAsync(farStore.Id, null, category.Id, 0, 10)).Items.Select(item => item.Id).ToArray());
            Assert.Equal([category.Id], await data.GetCarriedCategoryIdsAsync(farStore.Id));
            var carryingStores = await data.GetStoreIdsCarryingProductsAsync();
            Assert.Contains(store.Id, carryingStores);
            Assert.Contains(farStore.Id, carryingStores);

            var carriedPage = await service.GetCatalogProductsAsync(new CatalogProductQuery(token, null, 1, 10, farStore.Id, CarriedOnly: true));
            var farItem = Assert.Single(carriedPage.Page!.Items);
            Assert.Equal((beta.Id, true, false), (farItem.Id, farItem.InStock, farItem.LowStock));
            var storeCategories = await service.GetCatalogCategoriesForStoreAsync(farStore.Id);
            Assert.Contains(storeCategories.Categories!, item => item.Id == category.Id);

            // The store picker: both stores deliver to this point, nearest first; one 1.1 km away still estimates a little longer.
            var picker = await service.GetServiceableStoresAsync(new NearestStoreQuery(40.0, 40.0));
            var ours = picker.Stores.Where(item => item.Id == store.Id || item.Id == farStore.Id).ToArray();
            Assert.Equal([store.Id, farStore.Id], ours.Select(item => item.Id).ToArray());
            Assert.True(ours[1].DistanceKm > ours[0].DistanceKm);
            Assert.True(ours[1].EstimatedMinutes >= ours[0].EstimatedMinutes);

            // The database enforces one default variant per product, a valid MRP and non-negative stock.
            await using var dupDb = new QuickCommerceDbContext(options);
            dupDb.ProductVariants.Add(new ProductVariant { ProductId = beta.Id, Sku = beta.Sku + "-DUP", Label = "x", Price = 1, IsDefault = true });
            await Assert.ThrowsAsync<DbUpdateException>(() => dupDb.SaveChangesAsync());
            await using var mrpDb = new QuickCommerceDbContext(options);
            mrpDb.ProductVariants.Add(new ProductVariant { ProductId = beta.Id, Sku = beta.Sku + "-BADMRP", Label = "y", Price = 10, Mrp = 5 });
            await Assert.ThrowsAsync<DbUpdateException>(() => mrpDb.SaveChangesAsync());
            await using var negDb = new QuickCommerceDbContext(options);
            negDb.StoreVariantInventory.Single(row => row.StoreId == store.Id && row.VariantId == betaVariant.Id).AvailableQuantity = -1;
            await Assert.ThrowsAsync<DbUpdateException>(() => negDb.SaveChangesAsync());

            // The database refuses an MRP below the price.
            await using var badDb = new QuickCommerceDbContext(options);
            badDb.Products.Add(new Product { Sku = $"{token}-X", Name = $"{token} Bad", Description = "bad", Price = 100, Mrp = 90, UnitOfMeasure = "1 kg", CategoryId = category.Id });
            await Assert.ThrowsAsync<DbUpdateException>(() => badDb.SaveChangesAsync());
        }
        finally
        {
            await using var cleanup = new QuickCommerceDbContext(options);
            await cleanup.StoreVariantInventory.Where(row => row.StoreId == store.Id || row.StoreId == farStore.Id).ExecuteDeleteAsync();
            await cleanup.ProductVariants.Where(row => row.ProductId == alpha.Id || row.ProductId == beta.Id || row.ProductId == retired.Id).ExecuteDeleteAsync();
            await cleanup.ProductTranslations.Where(row => row.ProductId == alpha.Id || row.ProductId == beta.Id || row.ProductId == retired.Id).ExecuteDeleteAsync();
            await cleanup.CategoryTranslations.Where(row => row.CategoryId == category.Id).ExecuteDeleteAsync();
            await cleanup.Products.Where(row => row.CategoryId == category.Id).ExecuteDeleteAsync();
            await cleanup.Stores.Where(row => row.Id == store.Id || row.Id == farStore.Id).ExecuteDeleteAsync();
            await cleanup.Categories.Where(row => row.Id == category.Id).ExecuteDeleteAsync();
        }
    }
}
