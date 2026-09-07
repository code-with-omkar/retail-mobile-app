using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using QuickCommerce.Api.Security;
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
}