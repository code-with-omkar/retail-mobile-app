using QuickCommerce.Application.DTOs;
using QuickCommerce.Domain;

namespace QuickCommerce.Application.Interfaces;

public sealed record GatewayOrder(string Id, long AmountPaise);

/// <param name="Status">The provider's word: "processed", "pending" or "failed".</param>
public sealed record GatewayRefund(string Id, string Status);

/// <param name="Status">The provider's word: "captured", "authorized", "created" or "failed".</param>
public sealed record GatewayPayment(string Id, string OrderId, long AmountPaise, string Status, long AmountRefundedPaise = 0);

/// <summary>The provider refused or could not be reached. <see cref="Code"/> is the provider's error code when it gave one.</summary>
public sealed class PaymentGatewayException(string message, string? code = null, bool transient = true) : Exception(message)
{
    public string? Code { get; } = code;

    /// <summary>True when trying again later may work (no connection, provider busy). False for a refusal.</summary>
    public bool Transient { get; } = transient;
}

/// <summary>The payment provider. Razorpay in production; a fake in tests. Nothing else in the application knows which provider it is.</summary>
public interface IPaymentGateway
{
    /// <param name="receipt">Our own reference (the order number), shown in the provider's dashboard.</param>
    Task<GatewayOrder> CreateOrderAsync(long amountPaise, string currency, string receipt, CancellationToken cancellationToken = default);

    /// <summary>Whether the result the app got from the payment screen really comes from the provider for this order and payment.</summary>
    bool VerifyPaymentSignature(string providerOrderId, string providerPaymentId, string signature);

    /// <summary>Whether a notification really comes from the provider. <paramref name="body"/> is the raw request body, exactly as received.</summary>
    bool VerifyWebhookSignature(string body, string signature);

    /// <param name="receipt">Our reference for this refund, so asking twice does not refund twice where the provider supports it.</param>
    Task<GatewayRefund> RefundAsync(string providerPaymentId, long amountPaise, string receipt, CancellationToken cancellationToken = default);

    Task<GatewayPayment?> FetchPaymentAsync(string providerPaymentId, CancellationToken cancellationToken = default);

    /// <summary>Every payment attempt made against one provider order.</summary>
    Task<IReadOnlyList<GatewayPayment>> FetchOrderPaymentsAsync(string providerOrderId, CancellationToken cancellationToken = default);
}

/// <summary>What the customer's payment screen needs to open, and nothing secret.</summary>
public sealed record StartPaymentResponse(
    string Provider,
    string KeyId,
    string ProviderOrderId,
    long AmountPaise,
    string Currency,
    DateTime ExpiresAt,
    string OrderNumber);

public sealed record ConfirmPaymentRequest(string ProviderOrderId, string ProviderPaymentId, string Signature);

public enum PaymentOperationStatus
{
    Succeeded,
    InvalidRequest,
    Unauthorized,
    NotFound,
    Conflict
}

/// <summary>Stable codes in the <c>reason</c> of a refused payment call.</summary>
public static class PaymentReasons
{
    public const string PaymentsUnavailable = "PaymentsUnavailable";
    public const string NotAnOnlineOrder = "NotAnOnlineOrder";
    public const string HoldExpired = "PaymentHoldExpired";
    public const string AlreadyPaid = "AlreadyPaid";
    public const string OrderNotAwaitingPayment = "OrderNotAwaitingPayment";
    public const string SignatureInvalid = "PaymentSignatureInvalid";
    public const string Mismatch = "PaymentMismatch";
    public const string AmountMismatch = "PaymentAmountMismatch";
    public const string NotCaptured = "PaymentNotCaptured";
    public const string ProviderUnavailable = "PaymentProviderUnavailable";
}

public sealed record PaymentOperationResult<T>(PaymentOperationStatus Status, T? Value = default, string? Message = null, string? Reason = null)
{
    public static PaymentOperationResult<T> Succeeded(T value) => new(PaymentOperationStatus.Succeeded, value);
    public static PaymentOperationResult<T> Invalid(string message) => new(PaymentOperationStatus.InvalidRequest, Message: message);
    public static PaymentOperationResult<T> Unauthorized(string message) => new(PaymentOperationStatus.Unauthorized, Message: message);
    public static PaymentOperationResult<T> NotFound(string message) => new(PaymentOperationStatus.NotFound, Message: message);
    public static PaymentOperationResult<T> Conflict(string message, string reason) => new(PaymentOperationStatus.Conflict, Message: message, Reason: reason);
}

/// <summary>The customer's side of paying for an order.</summary>
public interface ICustomerPaymentService
{
    /// <summary>Starts (or, within the hold, restarts) the payment and returns what the payment screen needs.</summary>
    Task<PaymentOperationResult<StartPaymentResponse>> StartAsync(Guid orderId, CancellationToken cancellationToken = default);

    /// <summary>The app reports the result of the payment screen. The order becomes a normal pending order. Repeating it changes nothing.</summary>
    Task<PaymentOperationResult<OrderResponse>> ConfirmAsync(Guid orderId, ConfirmPaymentRequest request, CancellationToken cancellationToken = default);
}

public enum WebhookStatus
{
    /// <summary>Handled (or deliberately ignored): the provider can stop sending it.</summary>
    Accepted,

    /// <summary>The signature does not match: not from the provider.</summary>
    Rejected,

    /// <summary>Could not be handled now: the provider should send it again.</summary>
    Retry
}

public interface IPaymentWebhookService
{
    /// <param name="body">The raw request body, exactly as received (the signature is over these bytes).</param>
    /// <param name="signature">The provider's signature header.</param>
    /// <param name="eventId">The provider's id for this notification (a header), if it sent one.</param>
    Task<WebhookStatus> HandleAsync(string body, string? signature, string? eventId, CancellationToken cancellationToken = default);
}

/// <summary>The background work: release held items, start refunds, look up payments that never settled.</summary>
public interface IPaymentMaintenance
{
    /// <summary>Cancels orders still awaiting payment past their hold and returns their stock. Returns how many.</summary>
    Task<int> ReleaseExpiredHoldsAsync(CancellationToken cancellationToken = default);

    /// <summary>Asks the provider to refund every payment queued for a refund. Returns how many were started.</summary>
    Task<int> StartRefundsAsync(CancellationToken cancellationToken = default);

    /// <summary>Looks up payments (and refunds) that were never settled by a notification, and settles them. Returns how many changed.</summary>
    Task<int> ReconcileAsync(CancellationToken cancellationToken = default);
}

public enum CapturedOutcome
{
    /// <summary>The order was awaiting payment and is now a pending order.</summary>
    Paid,

    /// <summary>Seen before: nothing changed.</summary>
    AlreadyPaid,

    /// <summary>The order was already cancelled or had expired: the money is to be refunded.</summary>
    LateRefund,

    /// <summary>The provider says a different amount was paid: the money is to be refunded.</summary>
    AmountMismatch,

    UnknownPayment
}

/// <summary>An order seen from the payment side, for the person who owns it.</summary>
public sealed record PaymentOrderView(Guid OrderId, string OrderNumber, OrderStatus Status, string PaymentMethod, PaymentState PaymentStatus, decimal TotalAmount, DateTime? PaymentExpiresAt);

/// <summary>Persistence for payments. Every change that touches an order and its payment happens in one transaction.</summary>
public interface IPaymentStore
{
    /// <summary>The customer's own order, or null (another customer's is simply not found).</summary>
    Task<PaymentOrderView?> GetOrderAsync(Guid orderId, Guid userId, Guid organizationId, CancellationToken cancellationToken = default);

    Task<Payment?> GetPaymentAsync(Guid orderId, CancellationToken cancellationToken = default);

    /// <summary>The payment of an order awaiting payment, counting this as another attempt. Null when the order does not exist.</summary>
    Task<Payment?> BeginAttemptAsync(Guid orderId, DateTime now, CancellationToken cancellationToken = default);

    /// <summary>Remembers the provider's order id on the payment. False when another request already set one (the caller reads it again).</summary>
    Task<bool> AttachProviderOrderAsync(Guid paymentId, string providerOrderId, CancellationToken cancellationToken = default);

    /// <summary>The provider says this payment was captured. Safe to call again and from several places at once.</summary>
    Task<CapturedOutcome> ApplyCapturedAsync(string providerOrderId, string providerPaymentId, long paidPaise, DateTime now, CancellationToken cancellationToken = default);

    /// <summary>The provider says an attempt failed. The customer may try again while the hold lasts. False when the payment is not known.</summary>
    Task<bool> ApplyFailedAsync(string providerOrderId, string? providerPaymentId, string reason, DateTime now, CancellationToken cancellationToken = default);

    /// <summary>Cancels orders awaiting payment whose hold has run out and returns their stock. Returns the order ids.</summary>
    Task<IReadOnlyList<Guid>> ReleaseExpiredAsync(DateTime now, int max, CancellationToken cancellationToken = default);

    /// <summary>Payments waiting for a refund to be started (queued, no refund id yet).</summary>
    Task<IReadOnlyList<Payment>> GetRefundsToStartAsync(int max, CancellationToken cancellationToken = default);

    Task SetRefundStartedAsync(Guid paymentId, string refundId, bool processed, DateTime now, CancellationToken cancellationToken = default);

    /// <summary>The refund could not be made. It stays queued unless the provider refused it for good.</summary>
    Task SetRefundFailedAsync(Guid paymentId, string reason, bool permanent, DateTime now, CancellationToken cancellationToken = default);

    /// <summary>The provider says the refund is done (or failed for good). Returns false when no payment has this payment id.</summary>
    Task<bool> ApplyRefundResultAsync(string providerPaymentId, string refundId, bool processed, DateTime now, CancellationToken cancellationToken = default);

    /// <summary>Payments started but never settled, and refunds started but never confirmed, older than the given time.</summary>
    Task<IReadOnlyList<Payment>> GetUnsettledAsync(DateTime olderThan, int max, CancellationToken cancellationToken = default);

    /// <summary>Records a provider notification as handled. False when it had been recorded before.</summary>
    Task<bool> TryRecordEventAsync(string provider, string eventId, string type, DateTime now, CancellationToken cancellationToken = default);
}
