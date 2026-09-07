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
    [HttpGet("{storeId:guid}")]
    public async Task<IActionResult> Get(Guid storeId, CancellationToken cancellationToken) => await cartService.GetCartAsync(storeId, cancellationToken) is { } cart
        ? Ok(new { success = true, data = cart })
        : NotFound(Failure("Cart not found"));

    [HttpPost("{storeId:guid}/items")]
    public async Task<IActionResult> Add(Guid storeId, AddCartItemRequest request, CancellationToken cancellationToken) => Respond(
        await cartService.AddItemAsync(storeId, request, cancellationToken));

    [HttpPut("{storeId:guid}/items/{productId:guid}")]
    public async Task<IActionResult> Update(Guid storeId, Guid productId, UpdateCartItemRequest request, CancellationToken cancellationToken) => Respond(
        await cartService.UpdateItemAsync(storeId, productId, request, cancellationToken));

    [HttpDelete("{storeId:guid}/items/{productId:guid}")]
    public async Task<IActionResult> Remove(Guid storeId, Guid productId, CancellationToken cancellationToken) => Respond(
        await cartService.RemoveItemAsync(storeId, productId, cancellationToken));

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