using QuickCommerce.Domain;

namespace QuickCommerce.Application.DTOs;

public sealed record CreateOrderRequest(
    Guid UserId,
    double Latitude,
    double Longitude,
    string DeliveryAddress,
    IReadOnlyList<OrderLineRequest> Items);

/// <param name="VariantId">The pack size. Omitted means the product's default variant.</param>
public sealed record OrderLineRequest(Guid ProductId, int Quantity, Guid? VariantId = null);

/// <summary>Stock is held per variant.</summary>
public sealed record InventoryAdjustment(Guid StoreId, Guid VariantId, int Quantity);

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
    IReadOnlyList<OrderStatusHistoryResponse> StatusHistory,
    decimal SubtotalAmount = 0,
    decimal DeliveryFee = 0,
    decimal HandlingFee = 0,
    string PaymentMethod = "CashOnDelivery",
    string? ReceiverName = null,
    string? ReceiverPhone = null,
    string? StoreName = null,
    string? StorePhone = null,
    int? EstimatedDeliveryMinutes = null);

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
    decimal TotalPrice,
    Guid? VariantId = null,
    string? VariantLabel = null);

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
