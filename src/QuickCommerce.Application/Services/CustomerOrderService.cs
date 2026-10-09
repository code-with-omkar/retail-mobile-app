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
        var stores = new Dictionary<Guid, Store?>();
        var result = new List<OrderResponse>();
        foreach (var order in orders.OrderByDescending(order => order.CreatedAt))
        {
            if (!stores.TryGetValue(order.StoreId, out var store))
            {
                store = await data.GetStoreAsync(order.StoreId, cancellationToken);
                stores[order.StoreId] = store;
            }

            result.Add(WithStore(Map(order), store));
        }

        return result;
    }

    public async Task<OrderResponse?> GetDetailsAsync(Guid orderId, CancellationToken cancellationToken = default)
    {
        var context = await currentUserContextResolver.ResolveAsync(cancellationToken);
        if (context is null || context.Role != Role.Customer)
        {
            return null;
        }

        var order = await data.GetCustomerOrderAsync(orderId, context.UserId, context.OrganizationId, cancellationToken);
        return order is null ? null : WithStore(Map(order), await data.GetStoreAsync(order.StoreId, cancellationToken));
    }

    public async Task<CancelOrderResult> CancelAsync(Guid orderId, CancellationToken cancellationToken = default)
    {
        var context = await currentUserContextResolver.ResolveAsync(cancellationToken);
        if (context is null || context.Role != Role.Customer)
        {
            return new CancelOrderResult(CancelOrderStatus.Unauthorized);
        }

        var result = await data.TryCancelCustomerOrderAsync(orderId, context.UserId, context.OrganizationId, cancellationToken);
        switch (result.Status)
        {
            case CancelCommitStatus.Cancelled or CancelCommitStatus.AlreadyCancelled:
                var order = result.Order!;
                return new CancelOrderResult(CancelOrderStatus.Succeeded, WithStore(order, await data.GetStoreAsync(order.StoreId, cancellationToken)));
            case CancelCommitStatus.NotCancellable:
                return new CancelOrderResult(
                    CancelOrderStatus.NotCancellable,
                    Message: "This order can no longer be cancelled because the store has already accepted it. Please contact the store.",
                    Reason: OrderCancelReasons.OrderNotCancellable);
            default:
                return new CancelOrderResult(CancelOrderStatus.NotFound, Message: "Order not found");
        }
    }

    /// <summary>The store's name and phone, so the customer knows who has the order and how to reach them.</summary>
    internal static OrderResponse WithStore(OrderResponse order, Store? store) => store is null ? order : order with { StoreName = store.Name, StorePhone = store.PhoneNumber };

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
        order.Items.Select(item => new OrderItemResponse(item.ProductId, item.ProductNameSnapshot, item.UnitPrice, item.Quantity, item.TotalPrice, item.VariantId, item.VariantLabelSnapshot)).ToArray(),
        order.StatusHistory.Select(history => new OrderStatusHistoryResponse(history.Status, history.ChangedAt)).ToArray(),
        order.SubtotalAmount,
        order.DeliveryFee,
        order.HandlingFee,
        order.PaymentMethod,
        order.ReceiverName,
        order.ReceiverPhone,
        EstimatedDeliveryMinutes: order.EstimatedDeliveryMinutes,
        PaymentStatus: order.PaymentStatus,
        PaymentExpiresAt: order.PaymentExpiresAt);
}
