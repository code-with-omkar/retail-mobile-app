using QuickCommerce.Application.DTOs;
using QuickCommerce.Application.Interfaces;
using QuickCommerce.Domain;

namespace QuickCommerce.Application.Services;

public sealed class CustomerOrderService(
    ICommerceStore data,
    ICurrentUserContextResolver currentUserContextResolver) : ICustomerOrderService
{
    public async Task<IReadOnlyList<OrderResponse>?> GetHistoryAsync(CancellationToken cancellationToken = default)
    {
        var context = await currentUserContextResolver.ResolveAsync(cancellationToken);
        if (context is null || context.Role != Role.Customer)
        {
            return null;
        }

        var orders = await data.GetCustomerOrdersAsync(context.UserId, context.OrganizationId, cancellationToken);
        return orders.OrderByDescending(order => order.CreatedAt).Select(Map).ToArray();
    }

    public async Task<OrderResponse?> GetDetailsAsync(Guid orderId, CancellationToken cancellationToken = default)
    {
        var context = await currentUserContextResolver.ResolveAsync(cancellationToken);
        if (context is null || context.Role != Role.Customer)
        {
            return null;
        }

        var order = await data.GetCustomerOrderAsync(orderId, context.UserId, context.OrganizationId, cancellationToken);
        return order is null ? null : Map(order);
    }

    private static OrderResponse Map(Order order) => new(
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