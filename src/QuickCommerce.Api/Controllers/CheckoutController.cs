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
    [HttpPost("{storeId:guid}")]
    public async Task<IActionResult> Checkout(Guid storeId, CheckoutRequest request, CancellationToken cancellationToken)
    {
        var result = await checkoutService.CheckoutAsync(storeId, request, cancellationToken);
        return result.Status switch
        {
            CheckoutOperationStatus.Succeeded => Created($"/api/orders/{result.Order!.Id}", new { success = true, data = result.Order }),
            CheckoutOperationStatus.InvalidRequest => BadRequest(Failure(result.Message!)),
            CheckoutOperationStatus.Unauthorized => Forbid(),
            CheckoutOperationStatus.NotFound => NotFound(Failure(result.Message!)),
            CheckoutOperationStatus.Conflict => Conflict(Failure(result.Message!, result.Reason)),
            _ => StatusCode(StatusCodes.Status500InternalServerError)
        };
    }

    // reason is a stable machine-readable code (for example OutsideServiceArea) next to the human message.
    private static object Failure(string message, string? reason = null) => new { success = false, message, reason, errors = Array.Empty<string>() };
}