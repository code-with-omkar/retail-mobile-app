namespace QuickCommerce.Application.DTOs;

public sealed record NotificationResponse(
    Guid Id,
    Guid? OrderId,
    string Type,
    string Title,
    string Message,
    bool IsRead,
    DateTime CreatedAt,
    string Category = "Order");

public sealed record MarkNotificationReadRequest(bool IsRead = true);

public sealed record UnreadCountResponse(int Count);

public enum CancelOrderStatus
{
    Succeeded,
    Unauthorized,
    NotFound,
    NotCancellable
}

/// <summary>Stable code in the <c>reason</c> of a refused cancellation.</summary>
public static class OrderCancelReasons
{
    public const string OrderNotCancellable = "OrderNotCancellable";
}

public sealed record CancelOrderResult(CancelOrderStatus Status, OrderResponse? Order = null, string? Message = null, string? Reason = null);

public enum CancelCommitStatus
{
    Cancelled,

    /// <summary>It was already cancelled: the same answer as the first time, nothing changed again.</summary>
    AlreadyCancelled,
    NotFound,
    NotCancellable
}

public sealed record CancelCommitResult(CancelCommitStatus Status, OrderResponse? Order = null, Domain.OrderStatus? CurrentStatus = null);
