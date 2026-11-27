using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using QuickCommerce.Api.Security;
using QuickCommerce.Application.Interfaces;

namespace QuickCommerce.Api.Controllers;

/// <summary>The signed-in customer's saved products. The customer comes from the token, never from the request.</summary>
[ApiController]
[Route("api/customer/favourites")]
[Authorize(Policy = SecurityPolicies.Orders)]
public sealed class FavouritesController(IFavouriteService favourites) : ControllerBase
{
    [HttpGet]
    public async Task<IActionResult> List([FromQuery] Guid? storeId, CancellationToken cancellationToken) => Respond(await favourites.ListAsync(storeId, cancellationToken));

    [HttpGet("ids")]
    public async Task<IActionResult> Ids(CancellationToken cancellationToken) => Respond(await favourites.IdsAsync(cancellationToken));

    [HttpPut("{productId:guid}")]
    public async Task<IActionResult> Add(Guid productId, CancellationToken cancellationToken) => Respond(await favourites.AddAsync(productId, cancellationToken));

    [HttpDelete("{productId:guid}")]
    public async Task<IActionResult> Remove(Guid productId, CancellationToken cancellationToken) => Respond(await favourites.RemoveAsync(productId, cancellationToken));

    private IActionResult Respond<T>(FavouriteResult<T> result)
    {
        var body = new { success = result.Status == FavouriteStatus.Succeeded, data = result.Value, message = result.Message, errors = Array.Empty<string>() };
        return result.Status switch
        {
            FavouriteStatus.Succeeded => Ok(body),
            FavouriteStatus.Forbidden => StatusCode(StatusCodes.Status403Forbidden, body),
            FavouriteStatus.LimitReached => Conflict(body),
            _ => NotFound(body)
        };
    }
}
