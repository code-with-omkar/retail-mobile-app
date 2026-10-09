using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using QuickCommerce.Api.Security;
using QuickCommerce.Application.DTOs;
using QuickCommerce.Application.Interfaces;

namespace QuickCommerce.Api.Controllers;

[ApiController]
[Route("api/customer/orders")]
[Authorize(Policy = SecurityPolicies.Orders)]
public sealed class CustomerOrdersController(ICustomerOrderService customerOrderService) : ControllerBase
{
    [HttpGet]
    public async Task<IActionResult> History(CancellationToken cancellationToken)
    {
        var orders = await customerOrderService.GetHistoryAsync(cancellationToken);
        return orders is null
            ? Forbid()
            : Ok(new { success = true, data = orders });
    }

    [HttpGet("{orderId:guid}")]
    public async Task<IActionResult> Details(Guid orderId, CancellationToken cancellationToken)
    {
        var order = await customerOrderService.GetDetailsAsync(orderId, cancellationToken);
        return order is null
            ? NotFound(new { success = false, message = "Order not found", errors = Array.Empty<string>() })
            : Ok(new { success = true, data = order });
    }

    /// <summary>
    /// Cancels the customer's own order while the store has not accepted it. 409 with reason <c>OrderNotCancellable</c> afterwards.
    /// Cancelling twice returns the same cancelled order.
    /// </summary>
    [HttpPost("{orderId:guid}/cancel")]
    public async Task<IActionResult> Cancel(Guid orderId, CancellationToken cancellationToken)
    {
        var result = await customerOrderService.CancelAsync(orderId, cancellationToken);
        return result.Status switch
        {
            CancelOrderStatus.Succeeded => Ok(new { success = true, data = result.Order }),
            CancelOrderStatus.NotFound => NotFound(new { success = false, message = result.Message, errors = Array.Empty<string>() }),
            CancelOrderStatus.NotCancellable => Conflict(new { success = false, message = result.Message, reason = result.Reason, errors = Array.Empty<string>() }),
            _ => Forbid()
        };
    }
}
