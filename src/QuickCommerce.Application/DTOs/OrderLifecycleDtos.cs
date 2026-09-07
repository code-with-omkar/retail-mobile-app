using QuickCommerce.Domain;

namespace QuickCommerce.Application.DTOs;

public sealed record ChangeOrderStatusRequest(OrderStatus Status);

public enum OrderLifecycleStatus
{
    Succeeded,
    NotFound,
    InvalidTransition,
    ConcurrencyConflict
}

public sealed record OrderLifecycleResult(OrderLifecycleStatus Status, OrderResponse? Order = null);