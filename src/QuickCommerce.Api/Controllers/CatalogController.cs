using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using QuickCommerce.Api.Security;
using QuickCommerce.Application.DTOs;
using QuickCommerce.Application.Interfaces;

namespace QuickCommerce.Api.Controllers;

[ApiController]
[Route("api")]
public sealed class CatalogController(ICatalogService catalogService, IAuthorizationScopeService scopeService, ICommerceStore data) : ControllerBase
{
    [HttpGet("categories")]
    public async Task<IActionResult> Categories(CancellationToken cancellationToken) => Ok(new { success = true, data = await catalogService.GetCategoriesAsync(cancellationToken) });

    [HttpGet("products")]
    [Authorize(Policy = SecurityPolicies.AdminDataRead)]
    public async Task<IActionResult> Products([FromQuery] string? search, [FromQuery] Guid? categoryId, CancellationToken cancellationToken) => Ok(new { success = true, data = await catalogService.GetProductsAsync(search, categoryId, cancellationToken) });

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
