using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using QuickCommerce.Application.DTOs;
using QuickCommerce.Application.Interfaces;
using QuickCommerce.Api.Security;

namespace QuickCommerce.Api.Controllers;

[ApiController]
[Route("api/orders")]
[Authorize(Policy = SecurityPolicies.Orders)]
public sealed class OrdersController(
    IOrderService orderService,
    ICustomerOrderService customerOrderService,
    ICurrentUserContextResolver currentUserContextResolver) : ControllerBase
{
    [HttpGet]
    public async Task<IActionResult> Orders(CancellationToken cancellationToken)
    {
        var context = await currentUserContextResolver.ResolveAsync(cancellationToken);
        if (context?.Role == QuickCommerce.Domain.Role.Customer)
        {
            var customerOrders = await customerOrderService.GetHistoryAsync(cancellationToken);
            return customerOrders is null ? Forbid() : Ok(new { success = true, data = customerOrders });
        }

        return Ok(new { success = true, data = await orderService.GetOrdersAsync(cancellationToken) });
    }

    [HttpPost]
    public async Task<IActionResult> Create(CreateOrderRequest request, CancellationToken cancellationToken)
    {
        var result = await orderService.CreateOrderAsync(request, cancellationToken);
        return result.Status switch
        {
            CreateOrderStatus.Created => Created($"/api/orders/{result.Order!.Id}", new { success = true, data = result.Order }),
            CreateOrderStatus.InvalidRequest => BadRequest(Failure(result.Message!)),
            CreateOrderStatus.NoServiceableStore => Conflict(Failure(result.Message!)),
            CreateOrderStatus.InventoryConflict => Conflict(Failure(result.Message!)),
            _ => StatusCode(StatusCodes.Status500InternalServerError)
        };
    }

    [HttpGet("{id:guid}")]
    public async Task<IActionResult> Get(Guid id, CancellationToken cancellationToken)
    {
        var context = await currentUserContextResolver.ResolveAsync(cancellationToken);
        var order = context?.Role == QuickCommerce.Domain.Role.Customer
            ? await customerOrderService.GetDetailsAsync(id, cancellationToken)
            : await orderService.GetOrderAsync(id, cancellationToken);
        return order is { }
            ? Ok(new { success = true, data = order })
            : NotFound(new { success = false, message = "Order not found", errors = Array.Empty<string>() });
    }

    private static object Failure(string message) => new { success = false, message, errors = Array.Empty<string>() };
}
