using Microsoft.Extensions.Logging;
using QuickCommerce.Application.DTOs;
using QuickCommerce.Application.Interfaces;
using QuickCommerce.Domain;

namespace QuickCommerce.Application.Services;

public sealed class PaymentService(
    IPaymentStore store,
    IPaymentGateway gateway,
    PaymentSettings settings,
    ICurrentUserContextResolver currentUserContextResolver,
    ICustomerOrderService orders,
    TimeProvider time,
    ILogger<PaymentService> log) : ICustomerPaymentService
{
    public async Task<PaymentOperationResult<StartPaymentResponse>> StartAsync(Guid orderId, CancellationToken cancellationToken = default)
    {
        if (!settings.Enabled)
        {
            return PaymentOperationResult<StartPaymentResponse>.Conflict("Online payment is not available right now.", PaymentReasons.PaymentsUnavailable);
        }

        var context = await currentUserContextResolver.ResolveAsync(cancellationToken);
        if (context is null || context.Role != Role.Customer)
        {
            return PaymentOperationResult<StartPaymentResponse>.Unauthorized("The authenticated user cannot pay for orders");
        }

        var view = await store.GetOrderAsync(orderId, context.UserId, context.OrganizationId, cancellationToken);
        if (view is null)
        {
            return PaymentOperationResult<StartPaymentResponse>.NotFound("Order not found");
        }

        if (view.PaymentMethod != PaymentMethods.Online)
        {
            return PaymentOperationResult<StartPaymentResponse>.Conflict("This order is not paid online.", PaymentReasons.NotAnOnlineOrder);
        }

        var now = time.GetUtcNow().UtcDateTime;
        if (view.Status != OrderStatus.AwaitingPayment)
        {
            return view.PaymentStatus is PaymentState.Paid or PaymentState.Refunding or PaymentState.Refunded or PaymentState.RefundFailed
                ? PaymentOperationResult<StartPaymentResponse>.Conflict("This order has already been paid.", PaymentReasons.AlreadyPaid)
                : view.Status == OrderStatus.Cancelled
                    ? PaymentOperationResult<StartPaymentResponse>.Conflict("The time to pay for this order has run out.", PaymentReasons.HoldExpired)
                    : PaymentOperationResult<StartPaymentResponse>.Conflict("This order is not waiting for payment.", PaymentReasons.OrderNotAwaitingPayment);
        }

        if (view.PaymentExpiresAt is null || view.PaymentExpiresAt <= now)
        {
            return PaymentOperationResult<StartPaymentResponse>.Conflict("The time to pay for this order has run out.", PaymentReasons.HoldExpired);
        }

        var payment = await store.BeginAttemptAsync(orderId, now, cancellationToken);
        if (payment is null)
        {
            return PaymentOperationResult<StartPaymentResponse>.NotFound("Payment not found");
        }

        // One provider order per payment: trying again reuses it, so the customer is never charged twice for one order.
        if (payment.ProviderOrderId is null)
        {
            try
            {
                var created = await gateway.CreateOrderAsync(payment.AmountPaise, payment.Currency, view.OrderNumber, cancellationToken);
                if (!await store.AttachProviderOrderAsync(payment.Id, created.Id, cancellationToken))
                {
                    payment = await store.GetPaymentAsync(orderId, cancellationToken) ?? payment;
                }
                else
                {
                    payment.ProviderOrderId = created.Id;
                }
            }
            catch (PaymentGatewayException ex)
            {
                log.LogWarning("Could not create the provider order for order {OrderNumber}: {Code}", view.OrderNumber, ex.Code);
                return PaymentOperationResult<StartPaymentResponse>.Conflict("We could not reach the payment service. Please try again in a moment.", PaymentReasons.ProviderUnavailable);
            }
        }

        return PaymentOperationResult<StartPaymentResponse>.Succeeded(new StartPaymentResponse(
            payment.Provider, settings.KeyId, payment.ProviderOrderId!, payment.AmountPaise, payment.Currency, view.PaymentExpiresAt.Value, view.OrderNumber));
    }

    public async Task<PaymentOperationResult<OrderResponse>> ConfirmAsync(Guid orderId, ConfirmPaymentRequest request, CancellationToken cancellationToken = default)
    {
        if (!settings.Enabled)
        {
            return PaymentOperationResult<OrderResponse>.Conflict("Online payment is not available right now.", PaymentReasons.PaymentsUnavailable);
        }

        if (string.IsNullOrWhiteSpace(request.ProviderOrderId) || string.IsNullOrWhiteSpace(request.ProviderPaymentId) || string.IsNullOrWhiteSpace(request.Signature) ||
            request.ProviderOrderId.Length > 64 || request.ProviderPaymentId.Length > 64 || request.Signature.Length > 200)
        {
            return PaymentOperationResult<OrderResponse>.Invalid("The payment details are incomplete.");
        }

        var context = await currentUserContextResolver.ResolveAsync(cancellationToken);
        if (context is null || context.Role != Role.Customer)
        {
            return PaymentOperationResult<OrderResponse>.Unauthorized("The authenticated user cannot pay for orders");
        }

        var view = await store.GetOrderAsync(orderId, context.UserId, context.OrganizationId, cancellationToken);
        var payment = view is null ? null : await store.GetPaymentAsync(orderId, cancellationToken);
        if (view is null || payment is null)
        {
            return PaymentOperationResult<OrderResponse>.NotFound("Order not found");
        }

        // What we asked the provider for is what counts, not what the app says it paid.
        if (payment.ProviderOrderId is null || !string.Equals(payment.ProviderOrderId, request.ProviderOrderId, StringComparison.Ordinal))
        {
            return PaymentOperationResult<OrderResponse>.Conflict("This payment does not belong to this order.", PaymentReasons.Mismatch);
        }

        if (!gateway.VerifyPaymentSignature(payment.ProviderOrderId, request.ProviderPaymentId, request.Signature))
        {
            log.LogWarning("A payment result with a bad signature was sent for order {OrderNumber}", view.OrderNumber);
            return PaymentOperationResult<OrderResponse>.Conflict("We could not verify this payment.", PaymentReasons.SignatureInvalid);
        }

        // The signature proves the provider produced it; the provider's own record says what was actually paid.
        GatewayPayment? paid;
        try
        {
            paid = await gateway.FetchPaymentAsync(request.ProviderPaymentId, cancellationToken);
        }
        catch (PaymentGatewayException ex)
        {
            log.LogWarning("Could not read payment for order {OrderNumber} from the provider: {Code}", view.OrderNumber, ex.Code);
            return PaymentOperationResult<OrderResponse>.Conflict("We could not confirm your payment yet. It will be confirmed automatically.", PaymentReasons.ProviderUnavailable);
        }

        if (paid is null || !string.Equals(paid.OrderId, payment.ProviderOrderId, StringComparison.Ordinal))
        {
            return PaymentOperationResult<OrderResponse>.Conflict("This payment does not belong to this order.", PaymentReasons.Mismatch);
        }

        if (!string.Equals(paid.Status, "captured", StringComparison.OrdinalIgnoreCase))
        {
            return PaymentOperationResult<OrderResponse>.Conflict("Your payment has not been completed yet.", PaymentReasons.NotCaptured);
        }

        var outcome = await store.ApplyCapturedAsync(payment.ProviderOrderId, paid.Id, paid.AmountPaise, time.GetUtcNow().UtcDateTime, cancellationToken);
        switch (outcome)
        {
            case CapturedOutcome.Paid or CapturedOutcome.AlreadyPaid:
                var order = await orders.GetDetailsAsync(orderId, cancellationToken);
                return order is null
                    ? PaymentOperationResult<OrderResponse>.NotFound("Order not found")
                    : PaymentOperationResult<OrderResponse>.Succeeded(order);
            case CapturedOutcome.LateRefund:
                return PaymentOperationResult<OrderResponse>.Conflict("Your payment arrived after the time to pay had run out. It will be refunded in full.", PaymentReasons.HoldExpired);
            case CapturedOutcome.AmountMismatch:
                return PaymentOperationResult<OrderResponse>.Conflict("The amount paid did not match the order. It will be refunded in full.", PaymentReasons.AmountMismatch);
            default:
                return PaymentOperationResult<OrderResponse>.NotFound("Payment not found");
        }
    }
}
