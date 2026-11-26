using QuickCommerce.Domain;

namespace QuickCommerce.Application.DTOs;

/// <summary><see cref="Reason"/> is required when rejecting (see OrderRejections); <see cref="Note"/> is required for Other.</summary>
public sealed record ChangeOrderStatusRequest(OrderStatus Status, string? Reason = null, string? Note = null);

public enum OrderLifecycleStatus
{
    Succeeded,
    NotFound,
    InvalidTransition,
    ConcurrencyConflict
}

public sealed record OrderLifecycleResult(OrderLifecycleStatus Status, OrderResponse? Order = null);