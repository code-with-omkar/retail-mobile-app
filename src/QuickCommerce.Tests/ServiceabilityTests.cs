using System.Security.Claims;
using System.Reflection;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using QuickCommerce.Api.Controllers;
using QuickCommerce.Api.Security;
using QuickCommerce.Application.DTOs;
using QuickCommerce.Application.Services;
using QuickCommerce.Application.Validators;
using QuickCommerce.Domain;
using QuickCommerce.Infrastructure;
using QuickCommerce.Infrastructure.Security;
using Xunit;

namespace QuickCommerce.Tests;

/// <summary>The "does a store deliver here" rule used by saved addresses, the public check and checkout.</summary>
public sealed class ServiceabilityTests
{
    private const double KmPerDegreeLatitude = 111.195;

    private static Store Harbor(InMemoryCommerceStore data) => data.Stores.Single(store => store.Name == "Harbor Point Dark Store");

    /// <summary>A point due north of a store at the given distance.</summary>
    private static GeoPoint North(Store store, double km) => new(store.Latitude + km / KmPerDegreeLatitude, store.Longitude);

    /// <summary>Only the Harbor Point store is active, so a single radius decides.</summary>
    private static (ServiceabilityService Service, InMemoryCommerceStore Data, Store Harbor) OneStore()
    {
        var data = InMemoryCommerceStore.CreateSeeded();
        foreach (var store in data.Stores.Where(store => store.Name != "Harbor Point Dark Store"))
        {
            store.IsActive = false;
        }

        return (new ServiceabilityService(data), data, Harbor(data));
    }

    private static async Task<ServiceabilityResponse> Check(ServiceabilityService service, GeoPoint point, Guid? organization = null) => (await service.CheckAsync([point], organization))[0];

    [Fact]
    public async Task At_a_stores_own_location_the_point_is_serviceable_at_distance_zero()
    {
        var (service, _, harbor) = OneStore();

        var answer = await Check(service, new GeoPoint(harbor.Latitude, harbor.Longitude));

        Assert.Equal((true, ServiceabilityReasons.Serviceable, 0.0), (answer.Serviceable, answer.Reason, answer.NearestDistanceKm));
    }

    [Theory]
    [InlineData(7.9, true)]
    [InlineData(7.99, true)]
    [InlineData(8.01, false)]
    [InlineData(8.1, false)]
    public async Task The_boundary_is_the_stores_service_radius(double km, bool serviceable)
    {
        var (service, _, harbor) = OneStore();
        Assert.Equal(8, harbor.ServiceRadiusKm);

        var answer = await Check(service, North(harbor, km));

        Assert.Equal(serviceable, answer.Serviceable);
        Assert.Equal(serviceable ? ServiceabilityReasons.Serviceable : ServiceabilityReasons.OutsideServiceArea, answer.Reason);
    }

    [Theory]
    [InlineData(7.9, true)]
    [InlineData(8.1, false)]
    public void The_store_covers_rule_matches_the_boundary(double km, bool covered)
    {
        var (_, _, harbor) = OneStore();
        var point = North(harbor, km);

        Assert.Equal(covered, ServiceabilityRules.StoreCovers(harbor, point.Latitude, point.Longitude));
    }

    [Fact]
    public async Task Outside_every_radius_the_answer_says_how_far_the_nearest_store_is()
    {
        var (service, _, harbor) = OneStore();

        var answer = await Check(service, North(harbor, 20));

        Assert.False(answer.Serviceable);
        Assert.Equal(ServiceabilityReasons.OutsideServiceArea, answer.Reason);
        Assert.InRange(answer.NearestDistanceKm!.Value, 19.9, 20.1);
    }

    [Fact]
    public async Task A_store_that_covers_the_point_but_carries_nothing_does_not_make_it_serviceable()
    {
        var (service, data, harbor) = OneStore();
        data.Inventory.RemoveAll(row => row.StoreId == harbor.Id);

        var answer = await Check(service, North(harbor, 2));

        Assert.False(answer.Serviceable);
        Assert.Equal(ServiceabilityReasons.NoStoreAvailable, answer.Reason);
        Assert.InRange(answer.NearestDistanceKm!.Value, 1.9, 2.1);
    }

    [Fact]
    public async Task A_second_store_that_carries_products_rescues_a_point_the_first_cannot_serve()
    {
        var data = InMemoryCommerceStore.CreateSeeded();
        var harbor = Harbor(data);
        data.Inventory.RemoveAll(row => row.StoreId == harbor.Id);
        var service = new ServiceabilityService(data);

        var answer = await Check(service, new GeoPoint(harbor.Latitude, harbor.Longitude));

        Assert.True(answer.Serviceable);
        Assert.True(answer.NearestDistanceKm > 0, "the answer names a store that carries products, not the empty one next door");
    }

    [Fact]
    public async Task Inactive_stores_are_ignored()
    {
        var (service, _, harbor) = OneStore();
        harbor.IsActive = false;

        var answer = await Check(service, new GeoPoint(harbor.Latitude, harbor.Longitude));

        Assert.Equal((false, ServiceabilityReasons.OutsideServiceArea, (double?)null), (answer.Serviceable, answer.Reason, answer.NearestDistanceKm));
    }

    [Fact]
    public async Task With_an_organization_only_its_stores_count()
    {
        var (service, data, harbor) = OneStore();
        var point = new GeoPoint(harbor.Latitude, harbor.Longitude);

        Assert.True((await Check(service, point, data.Organizations.Single().Id)).Serviceable);
        Assert.False((await Check(service, point, Guid.NewGuid())).Serviceable);
        Assert.True((await Check(service, point, null)).Serviceable);
    }

    [Fact]
    public async Task Many_points_are_answered_in_order()
    {
        var (service, _, harbor) = OneStore();

        var answers = await service.CheckAsync([North(harbor, 1), North(harbor, 50), North(harbor, 2), new GeoPoint(-33.9, 151.2)], null);

        Assert.Equal([true, false, true, false], answers.Select(answer => answer.Serviceable));
        Assert.Empty(await service.CheckAsync([], null));
    }

    // ---------- public route ----------

    [Fact]
    public void The_serviceability_route_is_public_and_the_address_routes_need_the_orders_policy()
    {
        var catalog = typeof(CatalogController).GetMethod(nameof(CatalogController.CatalogServiceability))!;
        Assert.Null(catalog.GetCustomAttribute<AuthorizeAttribute>());
        Assert.Null(typeof(CatalogController).GetCustomAttribute<AuthorizeAttribute>());
        Assert.Equal("catalog/serviceability", catalog.GetCustomAttribute<HttpGetAttribute>()!.Template);

        var addresses = typeof(CustomerAddressesController);
        Assert.Equal(SecurityPolicies.Orders, addresses.GetCustomAttribute<AuthorizeAttribute>()!.Policy);
        Assert.Equal("api/customer/addresses", addresses.GetCustomAttribute<RouteAttribute>()!.Template);
        Assert.Null(addresses.GetMethods().Select(method => method.GetCustomAttribute<AllowAnonymousAttribute>()).FirstOrDefault(attribute => attribute is not null));
    }

    [Theory]
    [InlineData(91, 0)]
    [InlineData(-91, 0)]
    [InlineData(0, 181)]
    [InlineData(0, -181)]
    [InlineData(double.NaN, 0)]
    public async Task The_public_route_rejects_impossible_coordinates(double latitude, double longitude)
    {
        var data = InMemoryCommerceStore.CreateSeeded();
        var controller = new CatalogController(null!, null!, data, new ServiceabilityService(data));

        var result = await controller.CatalogServiceability(latitude, longitude, CancellationToken.None);

        Assert.IsType<BadRequestObjectResult>(result);
    }

    [Fact]
    public async Task The_public_route_answers_with_the_reason()
    {
        var data = InMemoryCommerceStore.CreateSeeded();
        var controller = new CatalogController(null!, null!, data, new ServiceabilityService(data));

        var near = Assert.IsType<OkObjectResult>(await controller.CatalogServiceability(Harbor(data).Latitude, Harbor(data).Longitude, CancellationToken.None));
        var far = Assert.IsType<OkObjectResult>(await controller.CatalogServiceability(28.6, 77.2, CancellationToken.None));

        Assert.True(Data(near).Serviceable);
        Assert.Equal(ServiceabilityReasons.OutsideServiceArea, Data(far).Reason);

        static ServiceabilityResponse Data(OkObjectResult ok) => (ServiceabilityResponse)ok.Value!.GetType().GetProperty("data")!.GetValue(ok.Value)!;
    }

    // ---------- checkout (task 4.9) ----------

    private static (CartService Cart, CheckoutService Checkout) CartAndCheckout(InMemoryCommerceStore data)
    {
        var user = data.Users.Single();
        var currentUser = new HttpContextCurrentUser(new HttpContextAccessor
        {
            HttpContext = new DefaultHttpContext
            {
                User = new ClaimsPrincipal(new ClaimsIdentity(
                [
                    new Claim(ClaimTypes.NameIdentifier, user.ExternalSubject),
                    new Claim("organization_id", data.Organizations.Single().Id.ToString())
                ], "Test"))
            }
        });
        var resolver = new CurrentUserContextResolver(currentUser, data);
        return (new CartService(data, resolver, new AddCartItemRequestValidator(), new UpdateCartItemRequestValidator()), new CheckoutService(data, resolver, new CheckoutRequestValidator()));
    }

    [Fact]
    public async Task Checkout_refuses_an_address_the_store_does_not_deliver_to_and_changes_nothing()
    {
        var data = InMemoryCommerceStore.CreateSeeded();
        var harbor = Harbor(data);
        var tomato = data.Products.Single(product => product.Name == "Tomato");
        var stock = data.StockOf(harbor, tomato).AvailableQuantity;
        var (cart, checkout) = CartAndCheckout(data);
        await cart.AddItemAsync(harbor.Id, new AddCartItemRequest(tomato.Id, 2));
        var far = North(harbor, 30);

        var result = await checkout.CheckoutAsync(harbor.Id, new CheckoutRequest("30 km away", far.Latitude, far.Longitude));

        Assert.Equal(CheckoutOperationStatus.Conflict, result.Status);
        Assert.Equal(ServiceabilityReasons.OutsideServiceArea, result.Reason);
        Assert.Equal(stock, data.StockOf(harbor, tomato).AvailableQuantity);
        Assert.Empty(data.Orders);
        Assert.Equal(2, data.Carts.Single().Items.Single().Quantity);
    }

    [Theory]
    [InlineData(7.9, CheckoutOperationStatus.Succeeded)]
    [InlineData(8.1, CheckoutOperationStatus.Conflict)]
    public async Task Checkout_follows_the_stores_radius_at_the_boundary(double km, CheckoutOperationStatus expected)
    {
        var data = InMemoryCommerceStore.CreateSeeded();
        var harbor = Harbor(data);
        var tomato = data.Products.Single(product => product.Name == "Tomato");
        var (cart, checkout) = CartAndCheckout(data);
        await cart.AddItemAsync(harbor.Id, new AddCartItemRequest(tomato.Id, 1));
        var point = North(harbor, km);

        var result = await checkout.CheckoutAsync(harbor.Id, new CheckoutRequest("boundary", point.Latitude, point.Longitude));

        Assert.Equal(expected, result.Status);
    }

    [Fact]
    public async Task The_checkout_route_returns_the_reason_next_to_the_message()
    {
        var data = InMemoryCommerceStore.CreateSeeded();
        var harbor = Harbor(data);
        var tomato = data.Products.Single(product => product.Name == "Tomato");
        var (cart, checkout) = CartAndCheckout(data);
        await cart.AddItemAsync(harbor.Id, new AddCartItemRequest(tomato.Id, 1));
        var far = North(harbor, 30);

        var response = await new CheckoutController(checkout).Checkout(harbor.Id, new CheckoutRequest("far", far.Latitude, far.Longitude), CancellationToken.None);

        var conflict = Assert.IsType<ConflictObjectResult>(response);
        var body = conflict.Value!;
        Assert.Equal(ServiceabilityReasons.OutsideServiceArea, body.GetType().GetProperty("reason")!.GetValue(body));
        Assert.Equal(false, body.GetType().GetProperty("success")!.GetValue(body));
        Assert.False(string.IsNullOrWhiteSpace((string?)body.GetType().GetProperty("message")!.GetValue(body)));
    }
}
