namespace QuickCommerce.Application.DTOs;

/// <summary>
/// Where to deliver: either a saved address (<paramref name="AddressId"/>, the customer's own) or an address typed in with its point.
/// A saved address wins; the server copies its text, receiver and point onto the order.
/// </summary>
public sealed record CheckoutRequest(
    string? DeliveryAddress = null,
    double? Latitude = null,
    double? Longitude = null,
    Guid? AddressId = null,
    string? PaymentMethod = null);

/// <summary>The delivery details after the server has resolved them: what is stored on the order.</summary>
public sealed record CheckoutDelivery(
    string Address,
    double Latitude,
    double Longitude,
    Guid? AddressId = null,
    string? ReceiverName = null,
    string? ReceiverPhone = null);

/// <summary>Everything the data store needs to turn the cart into an order in one transaction.</summary>
/// <param name="IdempotencyKey">Null when the client sent none; there is then no duplicate protection.</param>
/// <param name="RequestHash">Fingerprint of the store and delivery address; the same key with another hash is refused.</param>
public sealed record CheckoutCommit(
    CheckoutDelivery Delivery,
    Services.PricingSettings Pricing,
    string? IdempotencyKey = null,
    string? RequestHash = null,
    int? EstimatedDeliveryMinutes = null,
    string PaymentMethod = Domain.PaymentMethods.CashOnDelivery,
    DateTime? PaymentExpiresAt = null);

public enum CheckoutCommitStatus
{
    Succeeded,
    CartNotFound,
    CartEmpty,
    ProductUnavailable,
    PriceChanged,
    InventoryConflict,
    KeyReused
}

/// <summary>A cart line that stopped checkout. Which fields are filled in depends on the reason.</summary>
/// <param name="Available">InventoryConflict: how many the store has left.</param>
/// <param name="OldPrice">PriceChanged: the price in the cart.</param>
/// <param name="NewPrice">PriceChanged: the price now.</param>
public sealed record CheckoutIssue(
    Guid ProductId,
    Guid? VariantId,
    string Name,
    string? Label,
    int Quantity,
    int? Available = null,
    decimal? OldPrice = null,
    decimal? NewPrice = null);

/// <param name="Replayed">The key had been used before and this is the order it produced, not a new one.</param>
public sealed record CheckoutCommitResult(
    CheckoutCommitStatus Status,
    OrderResponse? Order = null,
    IReadOnlyList<CheckoutIssue>? Issues = null,
    bool Replayed = false);

/// <summary>Stable codes in the <c>reason</c> field of a failed checkout, next to the human message.</summary>
public static class CheckoutReasons
{
    public const string CartEmpty = "CartEmpty";
    public const string ProductUnavailable = "ProductUnavailable";
    public const string PriceChanged = "PriceChanged";
    public const string InventoryConflict = "InventoryConflict";
    public const string IdempotencyKeyReused = "IdempotencyKeyReused";
    public const string PaymentsUnavailable = "PaymentsUnavailable";
}

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
    string? Message = null,
    string? Reason = null,
    IReadOnlyList<CheckoutIssue>? Details = null,
    bool Replayed = false)
{
    public static CheckoutOperationResult Succeeded(OrderResponse order, bool replayed = false) => new(CheckoutOperationStatus.Succeeded, order, Replayed: replayed);
    public static CheckoutOperationResult Invalid(string message) => new(CheckoutOperationStatus.InvalidRequest, Message: message);
    public static CheckoutOperationResult Unauthorized(string message) => new(CheckoutOperationStatus.Unauthorized, Message: message);
    public static CheckoutOperationResult NotFound(string message) => new(CheckoutOperationStatus.NotFound, Message: message);
    public static CheckoutOperationResult Conflict(string message, string? reason = null, IReadOnlyList<CheckoutIssue>? details = null) => new(CheckoutOperationStatus.Conflict, Message: message, Reason: reason, Details: details);
}
