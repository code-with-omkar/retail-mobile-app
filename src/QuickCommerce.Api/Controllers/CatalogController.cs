using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using QuickCommerce.Api.Security;
using QuickCommerce.Application.DTOs;
using QuickCommerce.Application.Interfaces;

namespace QuickCommerce.Api.Controllers;

[ApiController]
[Route("api")]
public sealed class CatalogController(ICatalogService catalogService, IAuthorizationScopeService scopeService, ICommerceStore data, IServiceabilityService serviceability, QuickCommerce.Application.Services.PricingSettings? pricing = null) : ControllerBase
{
    [HttpGet("categories")]
    public async Task<IActionResult> Categories(CancellationToken cancellationToken) => Ok(new { success = true, data = await catalogService.GetCategoriesAsync(cancellationToken) });

    [HttpGet("products")]
    [Authorize(Policy = SecurityPolicies.AdminDataRead)]
    public async Task<IActionResult> Products([FromQuery] string? search, [FromQuery] Guid? categoryId, CancellationToken cancellationToken) => Ok(new { success = true, data = await catalogService.GetProductsAsync(search, categoryId, cancellationToken) });

    /// <summary>Public, paged product list for customers. Anonymous by design; exposes only <see cref="CatalogProductResponse"/> fields.</summary>
    /// <summary>Public category list for customers, with translations.</summary>
    /// <summary>Optional <c>storeId</c> keeps only categories that have products the store carries.</summary>
    [HttpGet("catalog/categories")]
    public async Task<IActionResult> CatalogCategories([FromQuery] Guid? storeId = null, CancellationToken cancellationToken = default)
    {
        if (storeId is null)
        {
            return Ok(new { success = true, data = await catalogService.GetCatalogCategoriesAsync(cancellationToken) });
        }

        var result = await catalogService.GetCatalogCategoriesForStoreAsync(storeId.Value, cancellationToken);
        return result.Status == CatalogQueryStatus.Succeeded
            ? Ok(new { success = true, data = result.Categories })
            : NotFound(new { success = false, message = result.Message, errors = Array.Empty<string>() });
    }

    /// <summary>The fee settings, so a guest's cart shows the same delivery and handling fees and free-delivery threshold as the server will charge. Anonymous.</summary>
    [HttpGet("catalog/pricing")]
    public IActionResult Pricing()
    {
        var settings = pricing ?? new QuickCommerce.Application.Services.PricingSettings();
        return Ok(new { success = true, data = new { deliveryFee = settings.DeliveryFee, handlingFee = settings.HandlingFee, freeDeliveryThreshold = settings.FreeDeliveryThreshold } });
    }

    /// <summary>Whether anyone delivers to a point, and if not, why (OutsideServiceArea or NoStoreAvailable). Anonymous, so guests can use it.</summary>
    [HttpGet("catalog/serviceability")]
    public async Task<IActionResult> CatalogServiceability([FromQuery] double latitude, [FromQuery] double longitude, CancellationToken cancellationToken)
    {
        if (double.IsNaN(latitude) || double.IsNaN(longitude) || latitude is < -90 or > 90 || longitude is < -180 or > 180)
        {
            const string message = "Latitude must be between -90 and 90 and longitude between -180 and 180.";
            return BadRequest(new { success = false, message, errors = new[] { message } });
        }

        var answer = (await serviceability.CheckAsync([new GeoPoint(latitude, longitude)], null, cancellationToken))[0];
        return Ok(new { success = true, data = answer });
    }

    /// <summary>Stores that deliver to a location, nearest first. An empty list means none does. Anonymous.</summary>
    [HttpGet("catalog/stores")]
    public async Task<IActionResult> CatalogStores([FromQuery] double latitude, [FromQuery] double longitude, CancellationToken cancellationToken)
    {
        var result = await catalogService.GetServiceableStoresAsync(new NearestStoreQuery(latitude, longitude), cancellationToken);
        return result.Valid
            ? Ok(new { success = true, data = result.Stores })
            : BadRequest(new { success = false, message = result.Message, errors = new[] { result.Message } });
    }

    /// <summary>Public product detail for customers (active products only), with MRP, discount and translations.</summary>
    [HttpGet("catalog/products/{id:guid}")]
    public async Task<IActionResult> CatalogProduct(Guid id, [FromQuery] Guid? storeId = null, CancellationToken cancellationToken = default)
    {
        if (storeId is null)
        {
            return await catalogService.GetCatalogProductAsync(id, cancellationToken) is { } product
                ? Ok(new { success = true, data = product })
                : NotFound(new { success = false, message = "Product not found", errors = Array.Empty<string>() });
        }

        var result = await catalogService.GetCatalogProductForStoreAsync(id, storeId.Value, cancellationToken);
        return result.Status == CatalogQueryStatus.Succeeded
            ? Ok(new { success = true, data = result.Product })
            : NotFound(new { success = false, message = result.Message, errors = Array.Empty<string>() });
    }

    /// <summary>Nearest store that serves a location, with distance and delivery estimate. Anonymous.</summary>
    [HttpGet("catalog/stores/nearest")]
    public async Task<IActionResult> CatalogNearestStore([FromQuery] double latitude, [FromQuery] double longitude, CancellationToken cancellationToken)
    {
        var result = await catalogService.FindNearestStoreForCustomerAsync(new NearestStoreQuery(latitude, longitude), cancellationToken);
        return result.Status switch
        {
            NearestStoreStatus.Succeeded => Ok(new { success = true, data = result.Store }),
            NearestStoreStatus.InvalidRequest => BadRequest(new { success = false, message = result.Message, errors = new[] { result.Message } }),
            _ => NotFound(new { success = false, message = result.Message, errors = Array.Empty<string>() })
        };
    }

    /// <summary>Optional <c>storeId</c> adds inStock and lowStock flags for that store; with <c>carriedOnly=true</c> only products the store carries are returned.</summary>
    [HttpGet("catalog/products")]
    public async Task<IActionResult> CatalogProducts([FromQuery] string? search, [FromQuery] Guid? categoryId, [FromQuery] int page = 1, [FromQuery] int pageSize = CatalogProductQuery.DefaultPageSize, [FromQuery] Guid? storeId = null, [FromQuery] bool carriedOnly = false, CancellationToken cancellationToken = default)
    {
        var result = await catalogService.GetCatalogProductsAsync(new CatalogProductQuery(search, categoryId, page, pageSize, storeId, carriedOnly), cancellationToken);
        return result.Status switch
        {
            CatalogQueryStatus.Succeeded => Ok(new { success = true, data = result.Page }),
            CatalogQueryStatus.StoreNotFound => NotFound(new { success = false, message = result.Message, errors = Array.Empty<string>() }),
            _ => BadRequest(new { success = false, message = result.Message, errors = new[] { result.Message } })
        };
    }

    [HttpGet("products/{id:guid}")]
    public async Task<IActionResult> Product(Guid id, CancellationToken cancellationToken) => await catalogService.GetProductAsync(id, cancellationToken) is { } product
        ? Ok(new { success = true, data = product })
        : NotFound(new { success = false, message = "Product not found", errors = Array.Empty<string>() });

    [HttpGet("stores")]
    [Authorize(Policy = SecurityPolicies.AdminDataRead)]
    public async Task<IActionResult> Stores(CancellationToken cancellationToken)
    {
        var scope = await scopeService.ResolveAsync(cancellationToken);
        if (scope is null) return Forbid();
        var stores = await data.GetScopedStoresAsync(scope.OrganizationId, scope.StoreIds, scope.IsApplicationAdmin, cancellationToken);
        return Ok(new { success = true, data = stores.Select(store => new StoreResponse(store.Id, store.Name, store.Address, store.Latitude, store.Longitude, store.ServiceRadiusKm, store.IsActive)).ToArray() });
    }

    [HttpGet("stores/nearest")]
    public async Task<IActionResult> NearestStore([FromQuery] double latitude, [FromQuery] double longitude, [FromQuery] string? productIds, CancellationToken cancellationToken)
    {
        var requiredProducts = (productIds ?? string.Empty).Split(',', StringSplitOptions.RemoveEmptyEntries).Select(Guid.Parse).ToArray();
        var nearest = await catalogService.FindNearestStoreAsync(latitude, longitude, requiredProducts, cancellationToken);
        return nearest is null ? NotFound(new { success = false, message = "No serviceable store has the requested products", errors = Array.Empty<string>() }) : Ok(new { success = true, data = nearest });
    }
}
