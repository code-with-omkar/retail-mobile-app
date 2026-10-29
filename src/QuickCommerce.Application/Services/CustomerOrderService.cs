using QuickCommerce.Application.DTOs;
using QuickCommerce.Application.Interfaces;
using QuickCommerce.Domain;

namespace QuickCommerce.Application.Services;

public sealed class CustomerOrderService(
    ICommerceStore data,
    ICurrentUserContextResolver currentUserContextResolver,
    IDeliveryLookup? delivery = null,
    IOrderChangeStore? changes = null,
    IReplacementStore? replacements = null,
    TimeProvider? timeProvider = null) : ICustomerOrderService
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

            result.Add(await WithDeliveryAsync(WithStore(Map(order), store), order, cancellationToken));
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
        if (order is null)
        {
            return null;
        }

        var detail = await WithDeliveryAsync(WithStore(Map(order), await data.GetStoreAsync(order.StoreId, cancellationToken)), order, cancellationToken);
        // What the store changed while packing it, newest first, so the customer can see what they will receive and what was refunded.
        if (changes is not null)
        {
            detail = detail with { Changes = (await changes.ListAsync(order.Id, cancellationToken)).OrderByDescending(item => item.CreatedAt).Select(OrderChangeRules.ToInfo).ToArray() };
        }

        if (replacements is not null)
        {
            // An open proposal the customer can still answer; an overdue one is not shown (it is closed by the maintenance job).
            var now = (timeProvider ?? TimeProvider.System).GetUtcNow().UtcDateTime;
            var open = (await replacements.ListAsync(order.Id, cancellationToken)).FirstOrDefault(item => item.Status == "Pending" && item.ExpiresAt > now);
            detail = detail with { Replacement = open is null ? null : ReplacementRules.ToInfo(open) };
        }

        return detail;
    }

    /// <summary>While the order is out for delivery the customer sees who has it and the code to give them at the door. Nobody else is shown the code.</summary>
    private async Task<OrderResponse> WithDeliveryAsync(OrderResponse response, Order order, CancellationToken cancellationToken)
    {
        if (delivery is null || order.Status != OrderStatus.OutForDelivery || !order.DeliveryPartnerId.HasValue)
        {
            return response;
        }

        var partner = await delivery.GetPartnerAsync(order.DeliveryPartnerId.Value, cancellationToken);
        var first = partner?.FirstName;
        if (string.IsNullOrWhiteSpace(first))
        {
            first = partner?.DisplayName.Split(' ', StringSplitOptions.RemoveEmptyEntries).FirstOrDefault();
        }

        return response with { DeliveryPartnerName = first, DeliveryPartnerPhone = partner?.PhoneNumber, DeliveryOtp = order.DeliveryOtp };
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
        PaymentExpiresAt: order.PaymentExpiresAt,
        Note: order.OrderNote,
        OutOfStockChoice: order.OutOfStockChoice);
}
