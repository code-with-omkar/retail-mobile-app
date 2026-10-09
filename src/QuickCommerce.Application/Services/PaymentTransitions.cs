using QuickCommerce.Application.Interfaces;
using QuickCommerce.Domain;

namespace QuickCommerce.Application.Services;

/// <summary>The words of a notification, before it is given to a customer.</summary>
public sealed record NoteText(string Title, string Message);

public sealed record CapturedResult(CapturedOutcome Outcome, NoteText? Note = null);

/// <summary>
/// What happens to an order and its payment at each payment event. Pure rules on the entities, so the real store and the in-memory store
/// cannot disagree about them; the stores only load, save, return stock and write the notification.
/// </summary>
public static class PaymentTransitions
{
    /// <summary>Rupees to paise, the way the provider counts (no fractions of a paisa).</summary>
    public static long ToPaise(decimal amount) => (long)Math.Round(amount * 100m, MidpointRounding.AwayFromZero);

    /// <summary>
    /// The provider says the payment was captured. A payment seen before changes nothing. A payment for an order still awaiting it makes it a
    /// pending order, visible to the shop. A payment that arrives too late (the hold ran out and the order was cancelled), or for the wrong
    /// amount, is queued for a full refund and the order is left as it is.
    /// </summary>
    public static CapturedResult ApplyCaptured(Order order, Payment payment, string providerPaymentId, long paidPaise, DateTime now)
    {
        var settledBefore = payment.Status is PaymentState.Paid or PaymentState.Refunding or PaymentState.Refunded or PaymentState.RefundFailed;
        if (settledBefore)
        {
            return new CapturedResult(CapturedOutcome.AlreadyPaid);
        }

        payment.ProviderPaymentId = providerPaymentId;
        payment.UpdatedAt = now;

        if (paidPaise != payment.AmountPaise)
        {
            payment.Status = PaymentState.Refunding;
            payment.FailureReason = "AmountMismatch";
            order.PaymentStatus = PaymentState.Refunding;
            return new CapturedResult(CapturedOutcome.AmountMismatch, new NoteText("Payment problem", "The amount paid did not match your order, so it will be refunded."));
        }

        if (order.Status != OrderStatus.AwaitingPayment)
        {
            payment.Status = PaymentState.Refunding;
            payment.FailureReason = "Late";
            order.PaymentStatus = PaymentState.Refunding;
            return new CapturedResult(CapturedOutcome.LateRefund, new NoteText("Payment will be refunded", "Your payment arrived after your order was cancelled, so it will be refunded in full."));
        }

        payment.Status = PaymentState.Paid;
        payment.FailureReason = null;
        order.PaymentStatus = PaymentState.Paid;
        order.PaymentExpiresAt = null;
        order.Status = OrderStatus.Pending;
        order.StatusHistory.Add(new OrderStatusHistory { Status = OrderStatus.Pending });
        return new CapturedResult(CapturedOutcome.Paid, new NoteText("Payment received", "We received your payment. Your order has been placed."));
    }

    /// <summary>An attempt failed. The customer may try again while the hold lasts; a payment that is already settled is never undone.</summary>
    public static bool ApplyFailed(Order order, Payment payment, string? providerPaymentId, string reason, DateTime now)
    {
        if (payment.Status is not (PaymentState.Created or PaymentState.Failed))
        {
            return false;
        }

        payment.Status = PaymentState.Failed;
        payment.FailureReason = reason.Length <= 200 ? reason : reason[..200];
        payment.UpdatedAt = now;
        if (order.Status == OrderStatus.AwaitingPayment)
        {
            order.PaymentStatus = PaymentState.Failed;
        }

        return true;
    }

    /// <summary>The hold ran out: the order is cancelled (the store returns its stock) and the payment can no longer be started.</summary>
    public static NoteText ExpireHold(Order order, Payment? payment, DateTime now)
    {
        order.Status = OrderStatus.Cancelled;
        order.StatusHistory.Add(new OrderStatusHistory { Status = OrderStatus.Cancelled });
        order.PaymentStatus = PaymentState.Failed;
        if (payment is not null && payment.Status is PaymentState.Created or PaymentState.Failed)
        {
            payment.Status = PaymentState.Failed;
            payment.FailureReason = "Expired";
            payment.UpdatedAt = now;
        }

        return new NoteText("Payment not completed", "Your order was cancelled because the payment was not completed in time.");
    }

    /// <summary>
    /// A paid order is cancelled or declined: queue the full refund (the refund job calls the provider). Returns true when a refund was queued.
    /// An order that was never paid has nothing to refund.
    /// </summary>
    public static bool QueueRefundIfPaid(Order order, Payment? payment, DateTime now)
    {
        if (payment is null || payment.Status != PaymentState.Paid)
        {
            return false;
        }

        payment.Status = PaymentState.Refunding;
        payment.UpdatedAt = now;
        order.PaymentStatus = PaymentState.Refunding;
        return true;
    }

    /// <summary>The provider has started, finished or refused the refund.</summary>
    public static NoteText? ApplyRefundResult(Order order, Payment payment, string refundId, bool processed, DateTime now)
    {
        if (payment.Status is not (PaymentState.Refunding or PaymentState.RefundFailed))
        {
            return null;
        }

        payment.RefundId = refundId;
        payment.UpdatedAt = now;
        if (processed)
        {
            payment.Status = PaymentState.Refunded;
            order.PaymentStatus = PaymentState.Refunded;
            return new NoteText("Refund processed", "Your refund has been processed. It reaches your account in a few days.");
        }

        payment.Status = PaymentState.RefundFailed;
        order.PaymentStatus = PaymentState.RefundFailed;
        return null;
    }
}
