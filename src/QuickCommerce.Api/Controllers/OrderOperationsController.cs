using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using QuickCommerce.Api.Security;
using QuickCommerce.Application.DTOs;
using QuickCommerce.Application.Interfaces;

namespace QuickCommerce.Api.Controllers;

[ApiController]
[Route("api/operations/orders")]
[Authorize(Policy = SecurityPolicies.StoreOrderOperations)]
public sealed class OrderOperationsController(IOrderOperationsService orderOperationsService) : ControllerBase
{
    [HttpPost("{orderId:guid}/status")]
    public async Task<IActionResult> ChangeStatus(Guid orderId, ChangeOrderStatusRequest request, CancellationToken cancellationToken)
    {
        var result = await orderOperationsService.ChangeStatusAsync(orderId, request, cancellationToken);
        return result.Status switch
        {
            OrderOperationStatus.Succeeded => Ok(new { success = true, data = result.Order }),
            OrderOperationStatus.InvalidRequest => BadRequest(Failure(result.Message!)),
            OrderOperationStatus.Unauthorized => Forbid(),
            OrderOperationStatus.NotFound => NotFound(Failure(result.Message!)),
            OrderOperationStatus.InvalidTransition => Conflict(Failure(result.Message!)),
            OrderOperationStatus.ConcurrencyConflict => Conflict(Failure(result.Message!)),
            _ => StatusCode(StatusCodes.Status500InternalServerError)
        };
    }

    private static object Failure(string message) => new { success = false, message, errors = Array.Empty<string>() };
}