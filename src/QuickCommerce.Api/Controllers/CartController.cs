using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using QuickCommerce.Api.Security;
using QuickCommerce.Application.DTOs;
using QuickCommerce.Application.Interfaces;

namespace QuickCommerce.Api.Controllers;

[ApiController]
[Route("api/carts")]
[Authorize(Policy = SecurityPolicies.Orders)]
public sealed class CartController(ICartService cartService) : ControllerBase
{
    /// <summary>The customer's cart, in whichever store it is. 204 when there is none, so the app can tell "empty" from an error.</summary>
    [HttpGet("current")]
    public async Task<IActionResult> Current(CancellationToken cancellationToken) => await cartService.GetCurrentCartAsync(cancellationToken) is { } cart
        ? Ok(new { success = true, data = cart })
        : NoContent();

    [HttpGet("{storeId:guid}")]
    public async Task<IActionResult> Get(Guid storeId, CancellationToken cancellationToken) => await cartService.GetCartAsync(storeId, cancellationToken) is { } cart
        ? Ok(new { success = true, data = cart })
        : NotFound(Failure("Cart not found"));

    [HttpPost("{storeId:guid}/items")]
    public async Task<IActionResult> Add(Guid storeId, AddCartItemRequest request, CancellationToken cancellationToken) => Respond(
        await cartService.AddItemAsync(storeId, request, cancellationToken));

    /// <summary>Optional <c>variantId</c> names the pack size; without it the product's only line (or its default variant's) is meant.</summary>
    [HttpPut("{storeId:guid}/items/{productId:guid}")]
    public async Task<IActionResult> Update(Guid storeId, Guid productId, UpdateCartItemRequest request, [FromQuery] Guid? variantId, CancellationToken cancellationToken) => Respond(
        await cartService.UpdateItemAsync(storeId, productId, request, variantId, cancellationToken));

    [HttpDelete("{storeId:guid}/items/{productId:guid}")]
    public async Task<IActionResult> Remove(Guid storeId, Guid productId, [FromQuery] Guid? variantId, CancellationToken cancellationToken) => Respond(
        await cartService.RemoveItemAsync(storeId, productId, variantId, cancellationToken));

    /// <summary>Empties the cart. Succeeds when there was none.</summary>
    [HttpDelete("{storeId:guid}")]
    public async Task<IActionResult> Clear(Guid storeId, CancellationToken cancellationToken) => Respond(
        await cartService.ClearAsync(storeId, cancellationToken));

    /// <summary>Adds a device-side (guest) cart. <c>data</c> is <c>{ cart, notes }</c>: notes list what could not be added or was reduced.</summary>
    [HttpPost("{storeId:guid}/merge")]
    public async Task<IActionResult> Merge(Guid storeId, MergeCartRequest request, CancellationToken cancellationToken)
    {
        var result = await cartService.MergeAsync(storeId, request, cancellationToken);
        return result.Status == CartOperationStatus.Succeeded
            ? Ok(new { success = true, data = new { cart = result.Cart, notes = result.Notes ?? [] } })
            : Respond(result);
    }

    /// <summary>Brings the cart's prices up to date, once the customer has seen and accepted the change.</summary>
    [HttpPost("{storeId:guid}/reprice")]
    public async Task<IActionResult> Reprice(Guid storeId, CancellationToken cancellationToken) => Respond(
        await cartService.RepriceAsync(storeId, cancellationToken));

    private IActionResult Respond(CartOperationResult result) => result.Status switch
    {
        CartOperationStatus.Succeeded => Ok(new { success = true, data = result.Cart }),
        CartOperationStatus.InvalidRequest => BadRequest(Failure(result.Message!)),
        CartOperationStatus.Unauthorized => Forbid(),
        CartOperationStatus.NotFound => NotFound(Failure(result.Message!)),
        CartOperationStatus.Conflict => Conflict(Failure(result.Message!)),
        _ => StatusCode(StatusCodes.Status500InternalServerError)
    };

    private static object Failure(string message) => new { success = false, message, errors = Array.Empty<string>() };
}
