namespace QuickCommerce.Application.DTOs;

/// <param name="VariantId">The pack size. Omitted means the product's default variant, so clients written before variants keep working.</param>
public sealed record AddCartItemRequest(Guid ProductId, int Quantity, Guid? VariantId = null);

public sealed record UpdateCartItemRequest(int Quantity);

public sealed record CartResponse(
    Guid Id,
    Guid CustomerId,
    Guid StoreId,
    DateTime CreatedAt,
    DateTime UpdatedAt,
    IReadOnlyList<CartItemResponse> Items,
    decimal TotalAmount);

public sealed record CartItemResponse(
    Guid ProductId,
    string ProductNameSnapshot,
    decimal UnitPriceSnapshot,
    int Quantity,
    decimal TotalPrice,
    Guid? VariantId = null,
    string? VariantLabel = null);

public enum CartOperationStatus
{
    Succeeded,
    InvalidRequest,
    Unauthorized,
    NotFound,
    Conflict
}

public sealed record CartOperationResult(
    CartOperationStatus Status,
    CartResponse? Cart = null,
    string? Message = null)
{
    public static CartOperationResult Succeeded(CartResponse cart) => new(CartOperationStatus.Succeeded, cart);
    public static CartOperationResult Invalid(string message) => new(CartOperationStatus.InvalidRequest, Message: message);
    public static CartOperationResult Unauthorized(string message) => new(CartOperationStatus.Unauthorized, Message: message);
    public static CartOperationResult NotFound(string message) => new(CartOperationStatus.NotFound, Message: message);
    public static CartOperationResult Conflict(string message) => new(CartOperationStatus.Conflict, Message: message);
}