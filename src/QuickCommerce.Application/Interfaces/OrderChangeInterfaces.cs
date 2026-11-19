using QuickCommerce.Domain;

namespace QuickCommerce.Application.Interfaces;

/// <summary>The quantity a line should have after the change. Lower than it is now; 0 removes the line. Never higher.</summary>
public sealed record ItemChange(Guid ProductId, Guid? VariantId, int Quantity);

public sealed record ChangeOrderItemsRequest(IReadOnlyList<ItemChange> Changes, string? Reason = null);

public sealed record ChangedLine(string Name, string? Label, int From, int To, decimal UnitPrice);

/// <summary>What the store changed and what goes back to the customer. RefundStatus: None, Pending, Started, Processed or Failed.</summary>
public sealed record OrderChangeInfo(DateTime At, IReadOnlyList<ChangedLine> Lines, decimal RefundAmount, string RefundStatus, string? Reason);

public sealed record OrderChangeResponse(Guid OrderId, string OrderNumber, decimal NewSubtotal, decimal NewTotal, OrderChangeInfo Change);

public enum OrderChangeStatus { Succeeded, Forbidden, NotFound, Invalid, WrongState, Conflict }

public sealed record OrderChangeResult<T>(OrderChangeStatus Status, T? Value = default, string? Message = null, string? Reason = null);

public static class OrderChangeReasons
{
    /// <summary>Every item would go: cancel or decline the order instead.</summary>
    public const string UseCancel = "UseCancel";
}

public interface IOrderChangeStore
{
    /// <summary>Applies the change to a store's order in one step: items, totals, commission, the refund to give back, a notification. Null when there is no such order.</summary>
    Task<OrderChangeCommit?> ApplyAsync(Guid orderId, Guid organizationId, Guid? storeId, Guid changedBy, IReadOnlyList<ItemChange> changes, string? reason, DateTime now, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<OrderAdjustment>> ListAsync(Guid orderId, CancellationToken cancellationToken = default);

    /// <summary>Refunds waiting to be asked of the provider, with the provider payment id to refund against.</summary>
    Task<IReadOnlyList<(OrderAdjustment Adjustment, string ProviderPaymentId)>> GetRefundsToStartAsync(int max, CancellationToken cancellationToken = default);

    Task SetRefundStartedAsync(Guid adjustmentId, string refundId, bool processed, CancellationToken cancellationToken = default);

    /// <summary>A refund the provider refused for good is Failed (the admin handles it); a temporary problem leaves it Pending.</summary>
    Task SetRefundFailedAsync(Guid adjustmentId, string reason, bool permanent, CancellationToken cancellationToken = default);

    /// <summary>Started refunds, for the sweep that confirms them with the provider.</summary>
    Task<IReadOnlyList<(OrderAdjustment Adjustment, string ProviderPaymentId, long RefundedBefore)>> GetStartedRefundsAsync(int max, CancellationToken cancellationToken = default);

    Task MarkRefundProcessedAsync(Guid adjustmentId, CancellationToken cancellationToken = default);
}

public sealed record OrderChangeCommit(OrderChangeStatus Status, string? Message = null, string? Reason = null, OrderAdjustment? Adjustment = null, string? OrderNumber = null, decimal NewTotal = 0);

public interface IOrderChangeService
{
    /// <summary>Store staff (or an administrator) remove or reduce items of an order that is accepted or being packed.</summary>
    Task<OrderChangeResult<OrderChangeResponse>> ChangeAsync(Guid orderId, ChangeOrderItemsRequest request, CancellationToken cancellationToken = default);
}
