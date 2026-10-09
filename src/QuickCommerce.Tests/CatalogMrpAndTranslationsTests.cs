using System.Reflection;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.Extensions.DependencyInjection;
using QuickCommerce.Api.Controllers;
using QuickCommerce.Application;
using QuickCommerce.Application.DTOs;
using QuickCommerce.Application.Interfaces;
using QuickCommerce.Application.Services;
using QuickCommerce.Domain;
using QuickCommerce.Infrastructure;
using QuickCommerce.Infrastructure.Persistence;
using Xunit;

namespace QuickCommerce.Tests;

public sealed class CatalogMrpAndTranslationsTests
{
    private static (CatalogService Service, InMemoryCommerceStore Data, CountingCache Cache) Create()
    {
        var data = InMemoryCommerceStore.CreateSeeded();
        var cache = new CountingCache();
        return (new CatalogService(data, new StoreSelectionService(), cache), data, cache);
    }

    private static Product Tomato(InMemoryCommerceStore data) => data.Products.Single(product => product.Name == "Tomato");

    private static async Task<CatalogProductResponse> Listed(CatalogService service, string name)
    {
        var page = await service.GetCatalogProductsAsync(new CatalogProductQuery(null, null, 1, 50));
        return page.Page!.Items.Single(item => item.Name == name);
    }

    // ---- Task 1.3: MRP and discount ----

    [Theory]
    [InlineData(45, 55, 55, 18)]   // 18.18% rounds down
    [InlineData(7, 8, 8, 13)]      // 12.5% rounds away from zero
    [InlineData(100, 100, 100, 0)] // equal MRP: no discount
    [InlineData(100, 90, 100, 0)]  // MRP below price (bad data) is treated as no discount
    [InlineData(100, null, 100, 0)]
    public void Pricing_rules(int price, int? mrp, int expectedMrp, int expectedDiscount)
    {
        Assert.Equal(expectedMrp, CatalogPricing.EffectiveMrp(price, mrp));
        Assert.Equal(expectedDiscount, CatalogPricing.DiscountPercent(price, mrp));
    }

    [Fact]
    public async Task Listing_returns_mrp_and_discount_for_a_product_with_an_mrp_and_none_otherwise()
    {
        var (service, data, _) = Create();
        data.DefaultVariantOf(Tomato(data)).Mrp = Tomato(data).Price + 10;

        var tomato = await Listed(service, "Tomato");
        var potato = await Listed(service, "Potato");

        Assert.Equal(Tomato(data).Price + 10, tomato.Mrp);
        Assert.True(tomato.DiscountPercent > 0);
        Assert.Equal(potato.Price, potato.Mrp);
        Assert.Equal(0, potato.DiscountPercent);
    }

    // ---- Task 1.4: translations ----

    [Fact]
    public async Task Listing_returns_translations_by_lowercase_locale_and_empty_map_when_none()
    {
        var (service, data, _) = Create();
        data.ProductTranslations.Add(new ProductTranslation { ProductId = Tomato(data).Id, Locale = "MR", Name = "टोमॅटो", Description = "ताजे लाल टोमॅटो" });
        data.ProductTranslations.Add(new ProductTranslation { ProductId = Tomato(data).Id, Locale = "hi", Name = "टमाटर", Description = null });

        var tomato = await Listed(service, "Tomato");
        var potato = await Listed(service, "Potato");

        Assert.Equal(["hi", "mr"], tomato.Translations.Keys.Order().ToArray());
        Assert.Equal("टोमॅटो", tomato.Translations["mr"].Name);
        Assert.Null(tomato.Translations["hi"].Description);
        Assert.Empty(potato.Translations);
    }

    [Fact]
    public async Task Search_finds_a_product_by_its_translated_name()
    {
        var (service, data, _) = Create();
        data.ProductTranslations.Add(new ProductTranslation { ProductId = Tomato(data).Id, Locale = "mr", Name = "टोमॅटो" });

        var result = await service.GetCatalogProductsAsync(new CatalogProductQuery("टोमॅटो", null));

        Assert.Equal(["Tomato"], result.Page!.Items.Select(item => item.Name));
    }

    [Fact]
    public async Task Inactive_products_and_their_translations_are_never_returned()
    {
        var (service, data, _) = Create();
        data.ProductTranslations.Add(new ProductTranslation { ProductId = Tomato(data).Id, Locale = "mr", Name = "टोमॅटो" });
        Tomato(data).IsActive = false;

        var listing = await service.GetCatalogProductsAsync(new CatalogProductQuery("टोमॅटो", null));
        var detail = await service.GetCatalogProductAsync(Tomato(data).Id);

        Assert.Empty(listing.Page!.Items);
        Assert.Null(detail);
    }

    [Fact]
    public async Task Detail_returns_mrp_discount_and_translations_and_null_for_unknown_ids()
    {
        var (service, data, _) = Create();
        data.DefaultVariantOf(Tomato(data)).Mrp = Tomato(data).Price + 10;
        data.ProductTranslations.Add(new ProductTranslation { ProductId = Tomato(data).Id, Locale = "mr", Name = "टोमॅटो" });

        var detail = await service.GetCatalogProductAsync(Tomato(data).Id);
        var missing = await service.GetCatalogProductAsync(Guid.NewGuid());

        Assert.NotNull(detail);
        Assert.True(detail!.DiscountPercent > 0);
        Assert.Equal("टोमॅटो", detail.Translations["mr"].Name);
        Assert.Null(missing);
    }

    [Fact]
    public async Task Customer_categories_include_translations_and_skip_inactive_categories()
    {
        var (service, data, _) = Create();
        var dairy = data.Categories.Single(category => category.Name == "Dairy");
        data.CategoryTranslations.Add(new CategoryTranslation { CategoryId = dairy.Id, Locale = "mr", Name = "दुग्धजन्य" });
        data.Categories.Single(category => category.Name == "Fruits").IsActive = false;

        var categories = await service.GetCatalogCategoriesAsync();

        Assert.DoesNotContain(categories, category => category.Name == "Fruits");
        Assert.Equal("दुग्धजन्य", categories.Single(category => category.Id == dairy.Id).Translations["mr"].Name);
        Assert.Empty(categories.Single(category => category.Name == "Vegetables").Translations);
    }

    [Fact]
    public async Task Customer_detail_and_categories_are_cached()
    {
        var (service, data, cache) = Create();
        var id = Tomato(data).Id;

        await service.GetCatalogProductAsync(id);
        await service.GetCatalogCategoriesAsync();
        data.Products.Clear();
        data.Categories.Clear();
        var detail = await service.GetCatalogProductAsync(id);
        var categories = await service.GetCatalogCategoriesAsync();

        Assert.NotNull(detail);
        Assert.NotEmpty(categories);
        Assert.Equal(2, cache.SetCalls);
    }

    // ---- Routes ----

    [Fact]
    public void New_customer_routes_are_anonymous_and_existing_admin_routes_are_unchanged()
    {
        foreach (var (name, template) in new[] { (nameof(CatalogController.CatalogCategories), "catalog/categories"), (nameof(CatalogController.CatalogProduct), "catalog/products/{id:guid}") })
        {
            var action = typeof(CatalogController).GetMethod(name)!;
            Assert.Equal(template, action.GetCustomAttribute<HttpGetAttribute>()!.Template);
            Assert.Empty(action.GetCustomAttributes<AuthorizeAttribute>());
        }

        Assert.Equal("products", typeof(CatalogController).GetMethod(nameof(CatalogController.Products))!.GetCustomAttribute<HttpGetAttribute>()!.Template);
        Assert.Equal("categories", typeof(CatalogController).GetMethod(nameof(CatalogController.Categories))!.GetCustomAttribute<HttpGetAttribute>()!.Template);
    }

    [Fact]
    public void Admin_product_and_category_responses_did_not_change()
    {
        Assert.Equal(["CategoryId", "Description", "Id", "ImageUrl", "IsActive", "Name", "Price", "Sku", "UnitOfMeasure"], typeof(ProductResponse).GetProperties().Select(p => p.Name).Order().ToArray());
        Assert.Equal(["Id", "IsActive", "Name", "ParentCategoryId"], typeof(CategoryResponse).GetProperties().Select(p => p.Name).Order().ToArray());
    }

    // ---- Persistence model (no database connection is opened) ----

    [Fact]
    public void Model_has_mrp_check_constraint_and_translation_tables_with_composite_keys()
    {
        var options = new DbContextOptionsBuilder<QuickCommerceDbContext>().UseSqlServer("Server=(local);Database=ModelOnly;Trusted_Connection=True;TrustServerCertificate=True;").Options;
        using var db = new QuickCommerceDbContext(options);
        var model = db.GetService<IDesignTimeModel>().Model; // check constraints are only kept in the design-time model

        var product = model.FindEntityType(typeof(Product))!;
        Assert.Equal(18, product.FindProperty(nameof(Product.Mrp))!.GetPrecision());
        Assert.True(product.FindProperty(nameof(Product.Mrp))!.IsNullable);
        Assert.Contains(product.GetCheckConstraints(), check => check.Name == "CK_Products_Mrp_GreaterOrEqual_Price");

        var productTranslation = model.FindEntityType(typeof(ProductTranslation))!;
        Assert.Equal(["ProductId", "Locale"], productTranslation.FindPrimaryKey()!.Properties.Select(p => p.Name).ToArray());
        Assert.Equal(10, productTranslation.FindProperty(nameof(ProductTranslation.Locale))!.GetMaxLength());
        Assert.Equal(DeleteBehavior.Cascade, productTranslation.GetForeignKeys().Single().DeleteBehavior);

        var categoryTranslation = model.FindEntityType(typeof(CategoryTranslation))!;
        Assert.Equal(["CategoryId", "Locale"], categoryTranslation.FindPrimaryKey()!.Properties.Select(p => p.Name).ToArray());
    }

    private sealed class CountingCache : ICacheService
    {
        private readonly Dictionary<string, object> values = [];
        public int SetCalls { get; private set; }

        public Task<T?> GetAsync<T>(string key, CancellationToken cancellationToken = default) =>
            Task.FromResult(values.TryGetValue(key, out var value) ? (T?)value : default);

        public Task SetAsync<T>(string key, T value, TimeSpan ttl, CancellationToken cancellationToken = default)
        {
            SetCalls++;
            values[key] = value!;
            return Task.CompletedTask;
        }

        public Task RemoveAsync(string key, CancellationToken cancellationToken = default)
        {
            values.Remove(key);
            return Task.CompletedTask;
        }
    }
}
