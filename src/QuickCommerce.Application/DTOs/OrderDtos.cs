using QuickCommerce.Domain;

namespace QuickCommerce.Application.DTOs;

public sealed record CreateOrderRequest(
    Guid UserId,
    double Latitude,
    double Longitude,
    string DeliveryAddress,
    IReadOnlyList<OrderLineRequest> Items);

public sealed record OrderLineRequest(Guid ProductId, int Quantity);

public sealed record InventoryAdjustment(Guid StoreId, Guid ProductId, int Quantity);

public sealed record OrderResponse(
    Guid Id,
    string OrderNumber,
    Guid UserId,
    Guid StoreId,
    decimal TotalAmount,
    OrderStatus Status,
    string DeliveryAddress,
    double Latitude,
    double Longitude,
    DateTime CreatedAt,
    IReadOnlyList<OrderItemResponse> Items,
    IReadOnlyList<OrderStatusHistoryResponse> StatusHistory);

public sealed record AdminOrderResponse(
    Guid Id,
    string OrderNumber,
    Guid UserId,
    string Customer,
    Guid StoreId,
    string Store,
    decimal TotalAmount,
    OrderStatus Status,
    DateTime CreatedAt);

public sealed record OrderItemResponse(
    Guid ProductId,
    string ProductNameSnapshot,
    decimal UnitPrice,
    int Quantity,
    decimal TotalPrice);

public sealed record OrderStatusHistoryResponse(OrderStatus Status, DateTime ChangedAt);

public enum CreateOrderStatus
{
    Created,
    InvalidRequest,
    NoServiceableStore,
    InventoryConflict
}

public sealed record CreateOrderResult(CreateOrderStatus Status, OrderResponse? Order = null, string? Message = null)
{
    public static CreateOrderResult Created(OrderResponse order) => new(CreateOrderStatus.Created, order);
    public static CreateOrderResult Invalid(string message) => new(CreateOrderStatus.InvalidRequest, Message: message);
    public static CreateOrderResult NoStore(string message) => new(CreateOrderStatus.NoServiceableStore, Message: message);
    public static CreateOrderResult InventoryConflict(string message) => new(CreateOrderStatus.InventoryConflict, Message: message);
}
