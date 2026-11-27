using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using QuickCommerce.Api.Security;
using QuickCommerce.Application.Interfaces;

namespace QuickCommerce.Api.Controllers;

/// <summary>The store removes or reduces items of an order it is packing.</summary>
[ApiController]
public sealed class OrderChangesController(IOrderChangeService changes) : ControllerBase
{
    [HttpPost("api/operations/orders/{orderId:guid}/change")]
    [Authorize(Policy = SecurityPolicies.StoreOrderOperations)]
    public async Task<IActionResult> Change(Guid orderId, ChangeOrderItemsRequest request, CancellationToken cancellationToken)
    {
        var result = await changes.ChangeAsync(orderId, request, cancellationToken);
        var body = new { success = result.Status == OrderChangeStatus.Succeeded, data = result.Value, message = result.Message, reason = result.Reason, errors = Array.Empty<string>() };
        return result.Status switch
        {
            OrderChangeStatus.Succeeded => Ok(body),
            OrderChangeStatus.Forbidden => StatusCode(StatusCodes.Status403Forbidden, body),
            OrderChangeStatus.NotFound => NotFound(body),
            OrderChangeStatus.Invalid => BadRequest(body),
            _ => Conflict(body)
        };
    }
}
