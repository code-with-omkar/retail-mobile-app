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

    /// <summary>Cancels an accepted order, with a reason. Online payments are refunded.</summary>
    [HttpPost("{orderId:guid}/cancel")]
    public async Task<IActionResult> Cancel(Guid orderId, CancelOrderByStoreRequest request, CancellationToken cancellationToken) =>
        Respond(await orderOperationsService.CancelAsync(orderId, request, cancellationToken));

    private IActionResult Respond(OrderOperationResult result) => result.Status switch
    {
        OrderOperationStatus.Succeeded => Ok(new { success = true, data = result.Order }),
        OrderOperationStatus.InvalidRequest => BadRequest(Failure(result.Message!)),
        OrderOperationStatus.Unauthorized => Forbid(),
        OrderOperationStatus.NotFound => NotFound(Failure(result.Message!)),
        OrderOperationStatus.InvalidTransition or OrderOperationStatus.ConcurrencyConflict => Conflict(Failure(result.Message!)),
        _ => StatusCode(StatusCodes.Status500InternalServerError)
    };

    private static object Failure(string message) => new { success = false, message, errors = Array.Empty<string>() };
}