namespace QuickCommerce.Application.DTOs;

/// <param name="VariantId">The pack size. Omitted means the product's default variant, so clients written before variants keep working.</param>
public sealed record AddCartItemRequest(Guid ProductId, int Quantity, Guid? VariantId = null);

public sealed record UpdateCartItemRequest(int Quantity);

/// <summary>A cart built on the device (a guest's) to be added to the customer's server cart.</summary>
public sealed record MergeCartRequest(IReadOnlyList<AddCartItemRequest> Items);

/// <param name="TotalAmount">The sum of the lines, kept for clients written before fees. <paramref name="Total"/> is what the customer pays.</param>
/// <param name="AmountToFreeDelivery">How much more to add for free delivery; zero when already free or when there is no threshold.</param>
public sealed record CartResponse(
    Guid Id,
    Guid CustomerId,
    Guid StoreId,
    DateTime CreatedAt,
    DateTime UpdatedAt,
    IReadOnlyList<CartItemResponse> Items,
    decimal TotalAmount,
    decimal Subtotal = 0,
    decimal DeliveryFee = 0,
    decimal HandlingFee = 0,
    decimal Total = 0,
    decimal FreeDeliveryThreshold = 0,
    decimal AmountToFreeDelivery = 0);

/// <param name="UnitPriceSnapshot">The price when the line was added. <paramref name="CurrentUnitPrice"/> is the price now; checkout refuses when they differ.</param>
/// <param name="Available">How many the store can supply now; null when not known.</param>
/// <param name="Unavailable">The product or pack is gone, so checkout would refuse.</param>
public sealed record CartItemResponse(
    Guid ProductId,
    string ProductNameSnapshot,
    decimal UnitPriceSnapshot,
    int Quantity,
    decimal TotalPrice,
    Guid? VariantId = null,
    string? VariantLabel = null,
    int? Available = null,
    bool Unavailable = false,
    decimal? CurrentUnitPrice = null);

public static class CartNoteKinds
{
    /// <summary>The product or pack is no longer sold.</summary>
    public const string Unavailable = "Unavailable";

    /// <summary>The store has none left.</summary>
    public const string OutOfStock = "OutOfStock";

    /// <summary>Added, but fewer than asked for because the store has no more.</summary>
    public const string Reduced = "Reduced";
}

/// <summary>Something a merge did not do as asked, to be told to the customer.</summary>
public sealed record CartNote(Guid ProductId, Guid? VariantId, string Name, string? Label, string Kind, int Quantity);

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
    string? Message = null,
    IReadOnlyList<CartNote>? Notes = null)
{
    public static CartOperationResult Succeeded(CartResponse cart, IReadOnlyList<CartNote>? notes = null) => new(CartOperationStatus.Succeeded, cart, Notes: notes);
    public static CartOperationResult Invalid(string message) => new(CartOperationStatus.InvalidRequest, Message: message);
    public static CartOperationResult Unauthorized(string message) => new(CartOperationStatus.Unauthorized, Message: message);
    public static CartOperationResult NotFound(string message) => new(CartOperationStatus.NotFound, Message: message);
    public static CartOperationResult Conflict(string message) => new(CartOperationStatus.Conflict, Message: message);
}
