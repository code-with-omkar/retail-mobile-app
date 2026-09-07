using Microsoft.AspNetCore.Mvc;
using QuickCommerce.Application.Interfaces;

namespace QuickCommerce.Api.Controllers;

[ApiController]
[Route("api")]
public sealed class CatalogController(ICatalogService catalogService) : ControllerBase
{
    [HttpGet("categories")]
    public async Task<IActionResult> Categories(CancellationToken cancellationToken) => Ok(new { success = true, data = await catalogService.GetCategoriesAsync(cancellationToken) });

    [HttpGet("products")]
    public async Task<IActionResult> Products([FromQuery] string? search, [FromQuery] Guid? categoryId, CancellationToken cancellationToken) => Ok(new { success = true, data = await catalogService.GetProductsAsync(search, categoryId, cancellationToken) });

    [HttpGet("products/{id:guid}")]
    public async Task<IActionResult> Product(Guid id, CancellationToken cancellationToken) => await catalogService.GetProductAsync(id, cancellationToken) is { } product
        ? Ok(new { success = true, data = product })
        : NotFound(new { success = false, message = "Product not found", errors = Array.Empty<string>() });

    [HttpGet("stores")]
    public async Task<IActionResult> Stores(CancellationToken cancellationToken) => Ok(new { success = true, data = await catalogService.GetStoresAsync(cancellationToken) });

    [HttpGet("stores/nearest")]
    public async Task<IActionResult> NearestStore([FromQuery] double latitude, [FromQuery] double longitude, [FromQuery] string? productIds, CancellationToken cancellationToken)
    {
        var requiredProducts = (productIds ?? string.Empty).Split(',', StringSplitOptions.RemoveEmptyEntries).Select(Guid.Parse).ToArray();
        var nearest = await catalogService.FindNearestStoreAsync(latitude, longitude, requiredProducts, cancellationToken);
        return nearest is null ? NotFound(new { success = false, message = "No serviceable store has the requested products", errors = Array.Empty<string>() }) : Ok(new { success = true, data = nearest });
    }
}
