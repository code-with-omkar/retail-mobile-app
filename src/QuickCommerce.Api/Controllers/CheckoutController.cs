using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using QuickCommerce.Api.Security;
using QuickCommerce.Application.DTOs;
using QuickCommerce.Application.Interfaces;

namespace QuickCommerce.Api.Controllers;

[ApiController]
[Route("api/checkout")]
[Authorize(Policy = SecurityPolicies.Orders)]
public sealed class CheckoutController(ICheckoutService checkoutService) : ControllerBase
{
    /// <summary>
    /// Places the order. Send an <c>Idempotency-Key</c> header, one new value per attempt to order: a retry or double tap with the same key
    /// returns the order it produced (200) instead of a second one (201 is the first time).
    /// </summary>
    [HttpPost("{storeId:guid}")]
    public async Task<IActionResult> Checkout(Guid storeId, CheckoutRequest request, CancellationToken cancellationToken)
    {
        string? key = HttpContext?.Request.Headers.TryGetValue("Idempotency-Key", out var values) == true ? values.ToString().Trim() : null;
        var result = await checkoutService.CheckoutAsync(storeId, request, string.IsNullOrEmpty(key) ? null : key, cancellationToken);
        return result.Status switch
        {
            CheckoutOperationStatus.Succeeded when result.Replayed => Ok(new { success = true, data = result.Order, replayed = true }),
            CheckoutOperationStatus.Succeeded => Created($"/api/orders/{result.Order!.Id}", new { success = true, data = result.Order }),
            CheckoutOperationStatus.InvalidRequest => BadRequest(Failure(result.Message!)),
            CheckoutOperationStatus.Unauthorized => Forbid(),
            CheckoutOperationStatus.NotFound => NotFound(Failure(result.Message!)),
            CheckoutOperationStatus.Conflict => Conflict(Failure(result.Message!, result.Reason, result.Details)),
            _ => StatusCode(StatusCodes.Status500InternalServerError)
        };
    }

    // reason is a stable machine-readable code (for example PriceChanged) next to the human message; details names the cart lines involved.
    private static object Failure(string message, string? reason = null, IReadOnlyList<CheckoutIssue>? details = null) => new { success = false, message, reason, details = details ?? [], errors = Array.Empty<string>() };
}
