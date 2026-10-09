using Microsoft.Extensions.Logging;
using QuickCommerce.Application.Interfaces;
using QuickCommerce.Domain;

namespace QuickCommerce.Application.Services;

/// <summary>
/// The work no customer triggers: release the items of orders that were not paid in time, ask the provider for the refunds that are due,
/// and look up payments whose notification never arrived. Every step is safe to run twice and from several servers at once.
/// </summary>
public sealed class PaymentMaintenance(IPaymentStore store, IPaymentGateway gateway, PaymentSettings settings, TimeProvider time, ILogger<PaymentMaintenance> log) : IPaymentMaintenance
{
    private const int BatchSize = 50;

    public async Task<int> ReleaseExpiredHoldsAsync(CancellationToken cancellationToken = default)
    {
        var released = await store.ReleaseExpiredAsync(time.GetUtcNow().UtcDateTime, BatchSize, cancellationToken);
        if (released.Count > 0)
        {
            log.LogInformation("Released the items of {Count} orders that were not paid in time", released.Count);
        }

        return released.Count;
    }

    public async Task<int> StartRefundsAsync(CancellationToken cancellationToken = default)
    {
        var started = 0;
        foreach (var payment in await store.GetRefundsToStartAsync(BatchSize, cancellationToken))
        {
            var now = time.GetUtcNow().UtcDateTime;
            if (payment.ProviderPaymentId is null)
            {
                // Nothing was ever paid at the provider, so there is nothing to give back.
                await store.SetRefundFailedAsync(payment.Id, "No provider payment to refund", permanent: true, now, cancellationToken);
                continue;
            }

            try
            {
                var refund = await gateway.RefundAsync(payment.ProviderPaymentId, payment.AmountPaise, "refund-" + payment.Id.ToString("N"), cancellationToken);
                await store.SetRefundStartedAsync(payment.Id, refund.Id, string.Equals(refund.Status, "processed", StringComparison.OrdinalIgnoreCase), now, cancellationToken);
                started++;
            }
            catch (PaymentGatewayException ex)
            {
                log.LogWarning("Refund for payment {PaymentId} was not made ({Code}, transient: {Transient})", payment.Id, ex.Code, ex.Transient);
                await store.SetRefundFailedAsync(payment.Id, ex.Message, permanent: !ex.Transient, now, cancellationToken);
            }
        }

        return started;
    }

    public async Task<int> ReconcileAsync(CancellationToken cancellationToken = default)
    {
        var now = time.GetUtcNow().UtcDateTime;
        var changed = 0;
        foreach (var payment in await store.GetUnsettledAsync(now.AddMinutes(-settings.ReconcileAfterMinutes), BatchSize, cancellationToken))
        {
            try
            {
                if (payment.Status is PaymentState.Created or PaymentState.Failed && payment.ProviderOrderId is not null)
                {
                    var captured = (await gateway.FetchOrderPaymentsAsync(payment.ProviderOrderId, cancellationToken))
                        .FirstOrDefault(item => string.Equals(item.Status, "captured", StringComparison.OrdinalIgnoreCase));
                    if (captured is not null && await store.ApplyCapturedAsync(payment.ProviderOrderId, captured.Id, captured.AmountPaise, now, cancellationToken) is not CapturedOutcome.AlreadyPaid)
                    {
                        log.LogWarning("Payment {PaymentId} had been captured but the server was never told; settled now", payment.Id);
                        changed++;
                    }
                }
                else if (payment.Status == PaymentState.Refunding && payment.RefundId is not null && payment.ProviderPaymentId is not null)
                {
                    var current = await gateway.FetchPaymentAsync(payment.ProviderPaymentId, cancellationToken);
                    if (current is not null && current.AmountRefundedPaise >= payment.AmountPaise &&
                        await store.ApplyRefundResultAsync(payment.ProviderPaymentId, payment.RefundId, processed: true, now, cancellationToken))
                    {
                        changed++;
                    }
                }
            }
            catch (PaymentGatewayException ex)
            {
                log.LogWarning("Payment {PaymentId} could not be looked up ({Code})", payment.Id, ex.Code);
            }
        }

        return changed;
    }
}
