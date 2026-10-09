using System.Reflection;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using QuickCommerce.Api.Controllers;
using QuickCommerce.Application;
using QuickCommerce.Application.DTOs;
using QuickCommerce.Application.Interfaces;
using QuickCommerce.Application.Services;
using QuickCommerce.Domain;
using QuickCommerce.Infrastructure;
using Xunit;

namespace QuickCommerce.Tests;

/// <summary>Store selection: the stores that deliver to a location, and a store's own catalogue (carriedOnly).</summary>
public sealed class CatalogStoreSelectionTests
{
    private static (CatalogService Service, InMemoryCommerceStore Data, RecordingCache Cache) Create()
    {
        var data = InMemoryCommerceStore.CreateSeeded();
        var cache = new RecordingCache();
        return (new CatalogService(data, new StoreSelectionService(), cache, new DeliveryEstimator(new DeliverySettings())), data, cache);
    }

    private static Store Named(InMemoryCommerceStore data, string name) => data.Stores.Single(store => store.Name == name);
    private static Product ProductNamed(InMemoryCommerceStore data, string name) => data.Products.Single(product => product.Name == name);

    private static void StopCarrying(InMemoryCommerceStore data, Store store, params string[] productNames)
    {
        var ids = productNames.Select(name => data.DefaultVariantOf(ProductNamed(data, name)).Id).ToHashSet();
        data.Inventory.RemoveAll(row => row.StoreId == store.Id && ids.Contains(row.VariantId));
    }

    private static async Task<PagedResult<CatalogProductResponse>> CarriedPage(CatalogService service, Guid storeId, int page = 1, int pageSize = 50, Guid? categoryId = null, string? search = null)
    {
        var result = await service.GetCatalogProductsAsync(new CatalogProductQuery(search, categoryId, page, pageSize, storeId, CarriedOnly: true));
        Assert.Equal(CatalogQueryStatus.Succeeded, result.Status);
        return result.Page!;
    }

    // ---- stores that deliver to a location ----

    [Fact]
    public async Task Stores_are_listed_nearest_first_with_distance_and_estimate()
    {
        var (service, data, _) = Create();
        var harbor = Named(data, "Harbor Point Dark Store");

        var result = await service.GetServiceableStoresAsync(new NearestStoreQuery(harbor.Latitude, harbor.Longitude));

        Assert.True(result.Valid);
        Assert.Equal(harbor.Id, result.Stores[0].Id);
        Assert.Equal(0, result.Stores[0].DistanceKm);
        Assert.Equal(8, result.Stores[0].EstimatedMinutes);
        Assert.Equal(result.Stores.Select(store => store.DistanceKm).Order(), result.Stores.Select(store => store.DistanceKm));
        Assert.All(result.Stores, store => Assert.True(store.DistanceKm <= store.ServiceRadiusKm));
        Assert.All(result.Stores, store => Assert.False(string.IsNullOrWhiteSpace(store.Address)));
    }

    [Fact]
    public async Task A_location_no_store_serves_gets_an_empty_list_not_an_error()
    {
        var (service, _, _) = Create();

        var result = await service.GetServiceableStoresAsync(new NearestStoreQuery(28.6, 77.2));

        Assert.True(result.Valid);
        Assert.Empty(result.Stores);
    }

    [Theory]
    [InlineData(91, 0)]
    [InlineData(-91, 0)]
    [InlineData(0, 181)]
    [InlineData(double.NaN, 0)]
    public async Task Invalid_coordinates_are_rejected(double latitude, double longitude)
    {
        var (service, _, _) = Create();

        var result = await service.GetServiceableStoresAsync(new NearestStoreQuery(latitude, longitude));

        Assert.False(result.Valid);
        Assert.NotNull(result.Message);
    }

    [Fact]
    public async Task A_store_that_carries_nothing_or_is_inactive_is_not_offered()
    {
        var (service, data, _) = Create();
        var harbor = Named(data, "Harbor Point Dark Store");
        data.Inventory.RemoveAll(row => row.StoreId == Named(data, "Cedar Market Hub").Id);
        Named(data, "North Star Fulfillment").IsActive = false;

        var result = await service.GetServiceableStoresAsync(new NearestStoreQuery(harbor.Latitude, harbor.Longitude));

        Assert.Equal(["Harbor Point Dark Store"], result.Stores.Select(store => store.Name));
    }

    [Fact]
    public async Task A_store_whose_radius_does_not_reach_the_location_is_not_offered()
    {
        var (service, data, _) = Create();
        var harbor = Named(data, "Harbor Point Dark Store");
        var cedar = Named(data, "Cedar Market Hub");
        cedar.ServiceRadiusKm = 1;

        var result = await service.GetServiceableStoresAsync(new NearestStoreQuery(harbor.Latitude, harbor.Longitude));

        Assert.DoesNotContain(result.Stores, store => store.Id == cedar.Id);
    }

    [Fact]
    public void Store_routes_are_anonymous_like_the_rest_of_the_customer_catalogue()
    {
        var controller = typeof(CatalogController);
        Assert.Null(controller.GetCustomAttribute<AuthorizeAttribute>());
        foreach (var name in new[] { "CatalogStores", "CatalogCategories", "CatalogProducts" })
        {
            var method = controller.GetMethod(name)!;
            Assert.Null(method.GetCustomAttribute<AuthorizeAttribute>());
        }

        Assert.Equal("catalog/stores", controller.GetMethod("CatalogStores")!.GetCustomAttribute<HttpGetAttribute>()!.Template);
    }

    // ---- a store's own catalogue ----

    [Fact]
    public async Task Carried_only_lists_just_what_the_store_carries()
    {
        var (service, data, _) = Create();
        var cedar = Named(data, "Cedar Market Hub");
        StopCarrying(data, cedar, "Tomato", "Farm Milk");

        var page = await CarriedPage(service, cedar.Id);

        Assert.Equal(3, page.TotalCount);
        Assert.DoesNotContain(page.Items, item => item.Name is "Tomato" or "Farm Milk");
        Assert.All(page.Items, item => Assert.NotNull(item.InStock));
    }

    [Fact]
    public async Task Without_carried_only_the_whole_catalogue_is_still_returned()
    {
        var (service, data, _) = Create();
        var cedar = Named(data, "Cedar Market Hub");
        StopCarrying(data, cedar, "Tomato");

        var result = await service.GetCatalogProductsAsync(new CatalogProductQuery(null, null, 1, 50, cedar.Id));

        Assert.Equal(5, result.Page!.TotalCount);
        Assert.False(result.Page.Items.Single(item => item.Name == "Tomato").InStock);
    }

    [Fact]
    public async Task An_out_of_stock_product_is_still_listed_so_it_can_be_shown_greyed_out()
    {
        var (service, data, _) = Create();
        var harbor = Named(data, "Harbor Point Dark Store");
        data.StockOf(harbor, ProductNamed(data, "Tomato")).AvailableQuantity = 0;

        var page = await CarriedPage(service, harbor.Id);

        var tomato = page.Items.Single(item => item.Name == "Tomato");
        Assert.False(tomato.InStock);
        Assert.False(tomato.LowStock);
        Assert.Equal(5, page.TotalCount);
    }

    [Fact]
    public async Task Carried_only_pages_and_counts_are_for_the_store_catalogue()
    {
        var (service, data, _) = Create();
        var cedar = Named(data, "Cedar Market Hub");
        StopCarrying(data, cedar, "Tomato");

        var first = await CarriedPage(service, cedar.Id, page: 1, pageSize: 3);
        var second = await CarriedPage(service, cedar.Id, page: 2, pageSize: 3);

        Assert.Equal(4, first.TotalCount);
        Assert.True(first.HasMore);
        Assert.Equal(3, first.Items.Count);
        Assert.Single(second.Items);
        Assert.False(second.HasMore);
        Assert.Empty(first.Items.Select(item => item.Id).Intersect(second.Items.Select(item => item.Id)));
    }

    [Fact]
    public async Task Carried_only_respects_category_and_search()
    {
        var (service, data, _) = Create();
        var cedar = Named(data, "Cedar Market Hub");
        StopCarrying(data, cedar, "Tomato");
        var vegetables = data.Categories.Single(category => category.Name == "Vegetables").Id;

        Assert.Equal(["Potato"], (await CarriedPage(service, cedar.Id, categoryId: vegetables)).Items.Select(item => item.Name));
        Assert.Empty((await CarriedPage(service, cedar.Id, search: "tomato")).Items);
        Assert.Equal(["Potato"], (await CarriedPage(service, cedar.Id, search: "potato")).Items.Select(item => item.Name));
    }

    [Fact]
    public async Task Stores_with_different_catalogues_never_share_a_page()
    {
        var (service, data, cache) = Create();
        var harbor = Named(data, "Harbor Point Dark Store");
        var cedar = Named(data, "Cedar Market Hub");
        StopCarrying(data, cedar, "Tomato");

        var harborPage = await CarriedPage(service, harbor.Id);
        var cedarPage = await CarriedPage(service, cedar.Id);
        var harborAgain = await CarriedPage(service, harbor.Id);

        Assert.Equal(5, harborPage.TotalCount);
        Assert.Equal(4, cedarPage.TotalCount);
        Assert.Equal(5, harborAgain.TotalCount);
        Assert.Equal(0, cache.SetCalls);
    }

    [Fact]
    public async Task A_change_in_what_a_store_carries_shows_at_once()
    {
        var (service, data, _) = Create();
        var cedar = Named(data, "Cedar Market Hub");
        Assert.Equal(5, (await CarriedPage(service, cedar.Id)).TotalCount);

        StopCarrying(data, cedar, "Tomato");

        Assert.Equal(4, (await CarriedPage(service, cedar.Id)).TotalCount);
    }

    [Fact]
    public async Task Carried_only_needs_a_store_and_an_active_one()
    {
        var (service, data, _) = Create();

        var noStore = await service.GetCatalogProductsAsync(new CatalogProductQuery(null, null, 1, 20, null, CarriedOnly: true));
        Assert.Equal(CatalogQueryStatus.InvalidRequest, noStore.Status);

        var unknown = await service.GetCatalogProductsAsync(new CatalogProductQuery(null, null, 1, 20, Guid.NewGuid(), CarriedOnly: true));
        Assert.Equal(CatalogQueryStatus.StoreNotFound, unknown.Status);

        var inactive = Named(data, "Cedar Market Hub");
        inactive.IsActive = false;
        var closed = await service.GetCatalogProductsAsync(new CatalogProductQuery(null, null, 1, 20, inactive.Id, CarriedOnly: true));
        Assert.Equal(CatalogQueryStatus.StoreNotFound, closed.Status);
    }

    // ---- categories for a store ----

    [Fact]
    public async Task Categories_for_a_store_are_only_those_with_products_it_carries()
    {
        var (service, data, _) = Create();
        var cedar = Named(data, "Cedar Market Hub");
        StopCarrying(data, cedar, "Farm Milk");

        var result = await service.GetCatalogCategoriesForStoreAsync(cedar.Id);

        Assert.Equal(CatalogQueryStatus.Succeeded, result.Status);
        var names = result.Categories!.Select(category => category.Name).ToArray();
        Assert.DoesNotContain("Dairy", names);
        Assert.Contains("Vegetables", names);
        Assert.Contains("Fruits", names);
    }

    [Fact]
    public async Task A_carried_sub_category_keeps_its_parent_category_visible()
    {
        var (service, data, _) = Create();
        var cedar = Named(data, "Cedar Market Hub");
        var vegetables = data.Categories.Single(category => category.Name == "Vegetables");
        var leafy = new Category { Name = "Leafy", ParentCategoryId = vegetables.Id };
        data.Categories.Add(leafy);
        var spinach = new Product { Sku = "VEG-SPI", Name = "Spinach", Description = "Fresh spinach", Price = 20, UnitOfMeasure = "250 g", CategoryId = leafy.Id };
        data.Products.Add(spinach);
        var spinachVariant = new ProductVariant { ProductId = spinach.Id, Sku = spinach.Sku, Label = spinach.UnitOfMeasure, Price = spinach.Price, IsDefault = true };
        data.Variants.Add(spinachVariant);
        data.Inventory.Add(new StoreVariantInventory { StoreId = cedar.Id, VariantId = spinachVariant.Id, AvailableQuantity = 10 });
        StopCarrying(data, cedar, "Tomato", "Potato");

        var names = (await service.GetCatalogCategoriesForStoreAsync(cedar.Id)).Categories!.Select(category => category.Name).ToArray();

        Assert.Contains("Leafy", names);
        Assert.Contains("Vegetables", names);
    }

    [Fact]
    public async Task Categories_for_an_unknown_store_are_not_found()
    {
        var (service, _, _) = Create();

        Assert.Equal(CatalogQueryStatus.StoreNotFound, (await service.GetCatalogCategoriesForStoreAsync(Guid.NewGuid())).Status);
        Assert.Equal(CatalogQueryStatus.StoreNotFound, (await service.GetCatalogCategoriesForStoreAsync(Guid.Empty)).Status);
    }

    [Fact]
    public async Task Categories_without_a_store_are_unchanged()
    {
        var (service, data, _) = Create();

        Assert.Equal(data.Categories.Count, (await service.GetCatalogCategoriesAsync()).Count);
    }

    private sealed class RecordingCache : ICacheService
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
