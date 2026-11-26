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
    int? EstimatedDeliveryMinutes = null,
    PaymentState PaymentStatus = PaymentState.NotRequired,
    DateTime? PaymentExpiresAt = null,
    string? DeliveryPartnerName = null,
    string? DeliveryPartnerPhone = null,
    string? DeliveryOtp = null,
    string? Note = null,
    string? OutOfStockChoice = null,
    IReadOnlyList<Interfaces.OrderChangeInfo>? Changes = null,
    Interfaces.ReplacementInfo? Replacement = null,
    string? CouponCode = null,
    decimal CouponDiscount = 0,
    DateTime? DeliverySlotStart = null,
    DateTime? DeliverySlotEnd = null,
    decimal? StoreRating = null,
    int? StoreRatingCount = null,
    int? MyStoreStars = null,
    int? MyDeliveryStars = null,
    string? RejectionReason = null);

/// <summary>What the portal's order list asks for. A store outside the caller's own is simply empty.</summary>
public sealed record AdminOrderQuery(string? Search = null, QuickCommerce.Domain.OrderStatus? Status = null, Guid? StoreId = null, int Page = 1, int PageSize = 20)
{
    public const int MaxPageSize = 100;
    public AdminOrderQuery Clamped() => this with { Search = string.IsNullOrWhiteSpace(Search) ? null : Search.Trim(), Page = Math.Max(1, Page), PageSize = Math.Clamp(PageSize, 1, MaxPageSize) };
}

public sealed record AdminOrderStore(Guid Id, string Name);

public sealed record AdminOrderPage(IReadOnlyList<AdminOrderResponse> Items, int TotalCount, int Page, int PageSize, IReadOnlyList<AdminOrderStore> Stores);

public sealed record AdminOrderResponse(
    Guid Id,
    string OrderNumber,
    Guid UserId,
    string Customer,
    Guid StoreId,
    string Store,
    decimal TotalAmount,
    OrderStatus Status,
    DateTime CreatedAt,
    Guid? DeliveryPartnerId = null,
    string? DeliveryPartner = null,
    string? Note = null,
    string? OutOfStockChoice = null,
    DateTime? DeliverySlotStart = null,
    DateTime? DeliverySlotEnd = null,
    string? RejectionReason = null,
    string? RejectionNote = null,
    string? RejectedBy = null);

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
