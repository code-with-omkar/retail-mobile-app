using QuickCommerce.Application.Interfaces;
using QuickCommerce.Domain;

namespace QuickCommerce.Application.Services;

/// <summary>The store changes an order it is packing. Store staff only touch their own store's orders; an administrator any in the organization.</summary>
public sealed class OrderChangeService(
    IOrderChangeStore store,
    ICurrentUserContextResolver contextResolver,
    TimeProvider? timeProvider = null) : IOrderChangeService
{
    private readonly TimeProvider clock = timeProvider ?? TimeProvider.System;

    public async Task<OrderChangeResult<OrderChangeResponse>> ChangeAsync(Guid orderId, ChangeOrderItemsRequest request, CancellationToken cancellationToken = default)
    {
        var context = await contextResolver.ResolveAsync(cancellationToken);
        if (context is null || context.Role is not (Role.StoreStaff or Role.Admin or Role.ApplicationAdmin))
        {
            return new(OrderChangeStatus.Forbidden, Message: "Only store staff or an administrator can change an order.");
        }

        if (context.Role == Role.StoreStaff && context.StoreId is null)
        {
            return new(OrderChangeStatus.Forbidden, Message: "Store staff must be assigned to a store.");
        }

        var commit = await store.ApplyAsync(orderId, context.OrganizationId, context.Role == Role.StoreStaff ? context.StoreId : null, context.UserId, request.Changes ?? [], request.Reason, clock.GetUtcNow().UtcDateTime, cancellationToken);
        if (commit is null)
        {
            return new(OrderChangeStatus.NotFound, Message: "Order not found.");
        }

        if (commit.Status != OrderChangeStatus.Succeeded)
        {
            return new(commit.Status, Message: commit.Message, Reason: commit.Reason);
        }

        var info = OrderChangeRules.ToInfo(commit.Adjustment!);
        return new(OrderChangeStatus.Succeeded, new OrderChangeResponse(orderId, commit.OrderNumber!, commit.Adjustment!.NewSubtotal, commit.NewTotal, info));
    }
}
