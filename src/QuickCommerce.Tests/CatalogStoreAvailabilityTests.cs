using System.Reflection;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using QuickCommerce.Api.Controllers;
using QuickCommerce.Application;
using QuickCommerce.Application.DTOs;
using QuickCommerce.Application.Interfaces;
using QuickCommerce.Application.Services;
using QuickCommerce.Application.Validators;
using QuickCommerce.Domain;
using QuickCommerce.Infrastructure;
using Xunit;

namespace QuickCommerce.Tests;

public sealed class CatalogStoreAvailabilityTests
{
    private static (CatalogService Service, InMemoryCommerceStore Data, CountingCache Cache) Create(DeliverySettings? settings = null)
    {
        var data = InMemoryCommerceStore.CreateSeeded();
        var cache = new CountingCache();
        return (new CatalogService(data, new StoreSelectionService(), cache, new DeliveryEstimator(settings ?? new DeliverySettings())), data, cache);
    }

    private static Store Harbor(InMemoryCommerceStore data) => data.Stores.Single(store => store.Name == "Harbor Point Dark Store");
    private static Product Tomato(InMemoryCommerceStore data) => data.Products.Single(product => product.Name == "Tomato");
    private static StoreVariantInventory Stock(InMemoryCommerceStore data, Store store, Product product) => data.StockOf(store, product);

    private static async Task<CatalogProductResponse> Listed(CatalogService service, Guid? storeId, string name)
    {
        var result = await service.GetCatalogProductsAsync(new CatalogProductQuery(null, null, 1, 50, storeId));
        Assert.Equal(CatalogQueryStatus.Succeeded, result.Status);
        return result.Page!.Items.Single(item => item.Name == name);
    }

    // ---- Delivery estimate (task 1.6) ----

    [Theory]
    [InlineData(0, 8)]
    [InlineData(0.4, 9)]    // fractions round up
    [InlineData(1.2, 11)]
    [InlineData(5.38, 19)]
    [InlineData(7.48, 23)]
    [InlineData(-3, 8)]     // never below the base
    public void Estimate_is_base_plus_rounded_up_minutes_per_km(double distanceKm, int expectedMinutes) =>
        Assert.Equal(expectedMinutes, new DeliveryEstimator(new DeliverySettings { BasePrepMinutes = 8, MinutesPerKm = 2 }).EstimateMinutes(distanceKm));

    [Fact]
    public void Estimate_follows_the_configured_settings()
    {
        var estimator = new DeliveryEstimator(new DeliverySettings { BasePrepMinutes = 10, MinutesPerKm = 3 });
        Assert.Equal(10 + 6, estimator.EstimateMinutes(2));
    }

    [Theory]
    [InlineData(0, 2)]
    [InlineData(-5, 2)]
    [InlineData(241, 2)]
    [InlineData(8, 0)]
    [InlineData(8, -1)]
    [InlineData(8, 61)]
    [InlineData(8, double.NaN)]
    public void Invalid_delivery_settings_are_rejected_at_startup(int baseMinutes, double perKm) =>
        Assert.Throws<InvalidOperationException>(() => new DeliverySettings { BasePrepMinutes = baseMinutes, MinutesPerKm = perKm }.Validate());

    [Fact]
    public void Default_delivery_settings_are_valid() => new DeliverySettings().Validate();

    // ---- Nearest store (task 1.10) ----

    [Fact]
    public async Task Nearest_store_returns_name_distance_and_estimate()
    {
        var (service, data, _) = Create();
        var store = Harbor(data);

        var result = await service.FindNearestStoreForCustomerAsync(new NearestStoreQuery(store.Latitude, store.Longitude));

        Assert.Equal(NearestStoreStatus.Succeeded, result.Status);
        Assert.Equal(store.Id, result.Store!.Id);
        Assert.Equal(0, result.Store.DistanceKm);
        Assert.Equal(8, result.Store.EstimatedMinutes);
        Assert.Equal(store.ServiceRadiusKm, result.Store.ServiceRadiusKm);
    }

    [Fact]
    public async Task Nearest_store_for_the_demo_location_is_harbor_point_at_about_5_4_km_and_19_minutes()
    {
        var (service, _, _) = Create();

        var result = await service.FindNearestStoreForCustomerAsync(new NearestStoreQuery(19.0596, 72.8295));

        Assert.Equal("Harbor Point Dark Store", result.Store!.Name);
        Assert.InRange(result.Store.DistanceKm, 5.3, 5.5);
        Assert.Equal(19, result.Store.EstimatedMinutes);
    }

    [Fact]
    public async Task Nearest_store_is_none_outside_every_service_radius_and_ignores_inactive_stores()
    {
        var (service, data, _) = Create();

        var faraway = await service.FindNearestStoreForCustomerAsync(new NearestStoreQuery(0, 0));
        foreach (var store in data.Stores)
        {
            store.IsActive = false;
        }

        var allInactive = await service.FindNearestStoreForCustomerAsync(new NearestStoreQuery(19.076, 72.8777));

        Assert.Equal(NearestStoreStatus.NoServiceableStore, faraway.Status);
        Assert.Equal(NearestStoreStatus.NoServiceableStore, allInactive.Status);
    }

    [Theory]
    [InlineData(91, 0)]
    [InlineData(-91, 0)]
    [InlineData(0, 181)]
    [InlineData(0, -181)]
    [InlineData(double.NaN, 0)]
    public async Task Nearest_store_rejects_out_of_range_coordinates(double latitude, double longitude)
    {
        var (service, _, _) = Create();

        var result = await service.FindNearestStoreForCustomerAsync(new NearestStoreQuery(latitude, longitude));

        Assert.Equal(NearestStoreStatus.InvalidRequest, result.Status);
    }

    // ---- Stock status (task 1.2) ----

    [Fact]
    public async Task Stock_flags_are_in_stock_low_stock_and_out_of_stock_per_store()
    {
        var (service, data, _) = Create();
        var store = Harbor(data);
        var stock = Stock(data, store, Tomato(data));

        stock.AvailableQuantity = 40;
        var plenty = await Listed(service, store.Id, "Tomato");
        stock.AvailableQuantity = stock.ReorderThreshold;
        var atThreshold = await Listed(service, store.Id, "Tomato");
        stock.AvailableQuantity = 1;
        var fewLeft = await Listed(service, store.Id, "Tomato");
        stock.AvailableQuantity = 0;
        var none = await Listed(service, store.Id, "Tomato");

        Assert.Equal((true, false), (plenty.InStock, plenty.LowStock));
        Assert.Equal((true, true), (atThreshold.InStock, atThreshold.LowStock));
        Assert.Equal((true, true), (fewLeft.InStock, fewLeft.LowStock));
        Assert.Equal((false, false), (none.InStock, none.LowStock));
    }

    [Fact]
    public async Task A_store_with_no_stock_row_for_a_product_does_not_carry_it()
    {
        var (service, data, _) = Create();
        var store = Harbor(data);
        data.Inventory.Remove(Stock(data, store, Tomato(data)));

        var tomato = await Listed(service, store.Id, "Tomato");

        Assert.Equal((false, false), (tomato.InStock, tomato.LowStock));
    }

    [Fact]
    public async Task Without_a_store_the_flags_are_unknown()
    {
        var (service, _, _) = Create();

        var tomato = await Listed(service, null, "Tomato");

        Assert.Null(tomato.InStock);
        Assert.Null(tomato.LowStock);
    }

    [Fact]
    public async Task Unknown_empty_or_inactive_store_is_not_found_or_invalid()
    {
        var (service, data, _) = Create();
        var inactive = Harbor(data);
        inactive.IsActive = false;

        var unknown = await service.GetCatalogProductsAsync(new CatalogProductQuery(null, null, 1, 20, Guid.NewGuid()));
        var empty = await service.GetCatalogProductsAsync(new CatalogProductQuery(null, null, 1, 20, Guid.Empty));
        var deactivated = await service.GetCatalogProductsAsync(new CatalogProductQuery(null, null, 1, 20, inactive.Id));

        Assert.Equal(CatalogQueryStatus.StoreNotFound, unknown.Status);
        Assert.Equal(CatalogQueryStatus.InvalidRequest, empty.Status);
        Assert.Equal(CatalogQueryStatus.StoreNotFound, deactivated.Status);
    }

    [Fact]
    public async Task Stock_is_always_fresh_even_when_the_product_page_comes_from_the_cache()
    {
        var (service, data, cache) = Create();
        var store = Harbor(data);
        var stock = Stock(data, store, Tomato(data));
        stock.AvailableQuantity = 40;

        var before = await Listed(service, store.Id, "Tomato");
        stock.AvailableQuantity = 0;
        var after = await Listed(service, store.Id, "Tomato");

        Assert.True(before.InStock);
        Assert.False(after.InStock);
        Assert.Equal(1, cache.SetCalls); // the page was cached once and reused, yet stock changed
    }

    [Fact]
    public async Task The_cached_page_itself_never_holds_stock_flags()
    {
        var (service, data, _) = Create();

        await Listed(service, Harbor(data).Id, "Tomato");
        var withoutStore = await Listed(service, null, "Tomato");

        Assert.Null(withoutStore.InStock);
    }

    [Fact]
    public async Task Detail_for_a_store_reports_stock_and_distinguishes_missing_product_from_missing_store()
    {
        var (service, data, _) = Create();
        var store = Harbor(data);
        Stock(data, store, Tomato(data)).AvailableQuantity = 0;

        var ok = await service.GetCatalogProductForStoreAsync(Tomato(data).Id, store.Id);
        var noProduct = await service.GetCatalogProductForStoreAsync(Guid.NewGuid(), store.Id);
        var noStore = await service.GetCatalogProductForStoreAsync(Tomato(data).Id, Guid.NewGuid());

        Assert.Equal(CatalogQueryStatus.Succeeded, ok.Status);
        Assert.False(ok.Product!.InStock);
        Assert.Equal(CatalogQueryStatus.NotFound, noProduct.Status);
        Assert.Equal(CatalogQueryStatus.StoreNotFound, noStore.Status);
    }

    [Fact]
    public void The_public_response_never_exposes_a_quantity()
    {
        var names = typeof(CatalogProductResponse).GetProperties().Select(property => property.Name).Concat(typeof(NearestStoreResponse).GetProperties().Select(property => property.Name));

        Assert.DoesNotContain(names, name => name.Contains("Quantity", StringComparison.OrdinalIgnoreCase) || name.Contains("Threshold", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Store_id_validation_rejects_an_empty_guid()
    {
        var validator = new CatalogProductQueryValidator();

        Assert.False(validator.Validate(new CatalogProductQuery(null, null, 1, 20, Guid.Empty)).IsValid);
        Assert.True(validator.Validate(new CatalogProductQuery(null, null, 1, 20, Guid.NewGuid())).IsValid);
        Assert.True(validator.Validate(new CatalogProductQuery(null, null)).IsValid);
    }

    // ---- Routes ----

    [Fact]
    public void Nearest_store_route_is_anonymous_and_the_old_nearest_route_is_unchanged()
    {
        var action = typeof(CatalogController).GetMethod(nameof(CatalogController.CatalogNearestStore))!;

        Assert.Equal("catalog/stores/nearest", action.GetCustomAttribute<HttpGetAttribute>()!.Template);
        Assert.Empty(action.GetCustomAttributes<AuthorizeAttribute>());
        Assert.Equal("stores/nearest", typeof(CatalogController).GetMethod(nameof(CatalogController.NearestStore))!.GetCustomAttribute<HttpGetAttribute>()!.Template);
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
