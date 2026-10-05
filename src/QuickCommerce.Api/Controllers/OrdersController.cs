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
    ICurrentUserContextResolver currentUserContextResolver,
    IAuthorizationScopeService scopeService,
    ICommerceStore data) : ControllerBase
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

        var scope = await scopeService.ResolveAsync(cancellationToken);
        if (scope is null)
        {
            return Forbid();
        }

        var orders = await data.GetScopedOrderSummariesAsync(scope.OrganizationId, scope.StoreIds, scope.IsApplicationAdmin, cancellationToken);
        return Ok(new { success = true, data = orders });
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
            : await GetScopedOrderAsync(id, cancellationToken);
        return order is { }
            ? Ok(new { success = true, data = order })
            : NotFound(new { success = false, message = "Order not found", errors = Array.Empty<string>() });
    }

    private static object Failure(string message) => new { success = false, message, errors = Array.Empty<string>() };

    private async Task<OrderResponse?> GetScopedOrderAsync(Guid id, CancellationToken cancellationToken)
    {
        var scope = await scopeService.ResolveAsync(cancellationToken);
        if (scope is null) return null;
        var orders = await data.GetScopedOrdersAsync(scope.OrganizationId, scope.StoreIds, scope.IsApplicationAdmin, cancellationToken);
        return orders.FirstOrDefault(order => order.Id == id) is { } order ? Map(order) : null;
    }

    private static OrderResponse Map(QuickCommerce.Domain.Order order) => new(
        order.Id,
        order.OrderNumber,
        order.UserId,
        order.StoreId,
        order.TotalAmount,
        order.Status,
        order.DeliveryAddress,
        order.Latitude,
        order.Longitude,
        order.CreatedAt,
        order.Items.Select(item => new OrderItemResponse(item.ProductId, item.ProductNameSnapshot, item.UnitPrice, item.Quantity, item.TotalPrice)).ToArray(),
        order.StatusHistory.Select(history => new OrderStatusHistoryResponse(history.Status, history.ChangedAt)).ToArray());
}
