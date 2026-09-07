using QuickCommerce.Application.DTOs;

namespace QuickCommerce.Application.Interfaces;

public interface IOrderOperationsService
{
    Task<OrderOperationResult> ChangeStatusAsync(Guid orderId, ChangeOrderStatusRequest request, CancellationToken cancellationToken = default);
}

public enum OrderOperationStatus
{
    Succeeded,
    InvalidRequest,
    Unauthorized,
    NotFound,
    InvalidTransition,
    ConcurrencyConflict
}

public sealed record OrderOperationResult(OrderOperationStatus Status, OrderResponse? Order = null, string? Message = null)
{
    public static OrderOperationResult Succeeded(OrderResponse order) => new(OrderOperationStatus.Succeeded, order);
    public static OrderOperationResult Invalid(string message) => new(OrderOperationStatus.InvalidRequest, Message: message);
    public static OrderOperationResult Unauthorized(string message) => new(OrderOperationStatus.Unauthorized, Message: message);
    public static OrderOperationResult NotFound(string message) => new(OrderOperationStatus.NotFound, Message: message);
    public static OrderOperationResult InvalidTransition(string message) => new(OrderOperationStatus.InvalidTransition, Message: message);
    public static OrderOperationResult Conflict(string message) => new(OrderOperationStatus.ConcurrencyConflict, Message: message);
}