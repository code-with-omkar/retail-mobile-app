using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace QuickCommerce.Tests;

/// <summary>
/// Every route of the real API, checked for who may call it. A new endpoint that nobody put a policy on would be open to the world; this fails
/// until it is either protected or deliberately added to the list of open ones below. (Needs no database: only the route table is read.)
/// </summary>
public sealed class ApiRouteCoverageTests : IClassFixture<ApiHarness>
{
    private readonly ApiHarness api;

    public ApiRouteCoverageTests(ApiHarness api) => this.api = api;

    /// <summary>What a person needs no sign-in for: signing in or up, the public catalogue, health and the payment provider's webhook.</summary>
    private static readonly string[] OpenPrefixes =
    [
        "api/auth", "api/signin", "api/account/register", "api/account/password", "api/catalog", "api/categories", "api/products", "api/stores/nearest", "api/health", "health", "media",
        "api/analytics", "api/payments/options", "api/payments/razorpay/webhook", "api/config", "api/offers", "openapi",
    ];

    private IEnumerable<(string Route, string Methods, bool Open)> Routes()
    {
        var source = api.Services.GetRequiredService<EndpointDataSource>();
        foreach (var endpoint in source.Endpoints.OfType<RouteEndpoint>())
        {
            var methods = endpoint.Metadata.GetMetadata<HttpMethodMetadata>()?.HttpMethods ?? [];
            if (methods.Count == 0)
            {
                continue;
            }

            var open = endpoint.Metadata.GetMetadata<IAllowAnonymous>() is not null || endpoint.Metadata.GetOrderedMetadata<IAuthorizeData>().Count == 0;
            yield return ((endpoint.RoutePattern.RawText ?? string.Empty).TrimStart('/'), string.Join(",", methods), open);
        }
    }

    [Fact]
    public void Every_endpoint_asks_for_a_sign_in_unless_it_is_deliberately_open()
    {
        var surprises = Routes()
            .Where(route => route.Open && !OpenPrefixes.Any(prefix => route.Route.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)))
            .Select(route => $"{route.Methods} /{route.Route}")
            .Order()
            .ToArray();

        Assert.True(surprises.Length == 0, "These endpoints can be called without signing in. Protect them, or add their prefix to OpenPrefixes if that is intended:\n" + string.Join("\n", surprises));
    }

    [Fact]
    public void The_api_has_the_routes_this_test_is_meant_to_watch()
    {
        var all = Routes().Select(route => route.Route).ToArray();
        Assert.True(all.Length > 80, $"only {all.Length} routes were found; the route table was not read");
        foreach (var expected in new[] { "api/admin/coupons", "api/admin/delivery-slots", "api/carts/{storeId:guid}/coupon", "api/stores/{storeId:guid}/delivery-slots", "api/checkout/{storeId:guid}" })
        {
            Assert.Contains(expected, all);
        }
    }
}
