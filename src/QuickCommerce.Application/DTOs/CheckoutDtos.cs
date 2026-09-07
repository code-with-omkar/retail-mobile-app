namespace QuickCommerce.Application.DTOs;

public sealed record CheckoutRequest(
    string DeliveryAddress,
    double Latitude,
    double Longitude);

public enum CheckoutCommitStatus
{
    Succeeded,
    CartNotFound,
    CartEmpty,
    ProductUnavailable,
    PriceChanged,
    InventoryConflict
}

public sealed record CheckoutCommitResult(CheckoutCommitStatus Status, OrderResponse? Order = null);

public enum CheckoutOperationStatus
{
    Succeeded,
    InvalidRequest,
    Unauthorized,
    NotFound,
    Conflict
}

public sealed record CheckoutOperationResult(
    CheckoutOperationStatus Status,
    OrderResponse? Order = null,
    string? Message = null)
{
    public static CheckoutOperationResult Succeeded(OrderResponse order) => new(CheckoutOperationStatus.Succeeded, order);
    public static CheckoutOperationResult Invalid(string message) => new(CheckoutOperationStatus.InvalidRequest, Message: message);
    public static CheckoutOperationResult Unauthorized(string message) => new(CheckoutOperationStatus.Unauthorized, Message: message);
    public static CheckoutOperationResult NotFound(string message) => new(CheckoutOperationStatus.NotFound, Message: message);
    public static CheckoutOperationResult Conflict(string message) => new(CheckoutOperationStatus.Conflict, Message: message);
}