using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using QuickCommerce.Application.Interfaces;

namespace QuickCommerce.Application.Services;

/// <summary>
/// Handles notifications from the payment provider. This is what keeps an order right when the app was closed, lost its connection or never
/// reported a payment: the provider tells the server directly. Every step is safe to repeat, because the provider sends a notification again
/// until it gets an answer.
/// </summary>
public sealed class PaymentWebhookService(IPaymentStore store, IPaymentGateway gateway, TimeProvider time, ILogger<PaymentWebhookService> log) : IPaymentWebhookService
{
    private const string Provider = "Razorpay";

    public async Task<WebhookStatus> HandleAsync(string body, string? signature, string? eventId, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrEmpty(signature) || string.IsNullOrEmpty(body) || !gateway.VerifyWebhookSignature(body, signature))
        {
            log.LogWarning("A payment notification with a missing or wrong signature was refused");
            return WebhookStatus.Rejected;
        }

        try
        {
            using var document = JsonDocument.Parse(body);
            var root = document.RootElement;
            var type = root.TryGetProperty("event", out var t) && t.ValueKind == JsonValueKind.String ? t.GetString() ?? string.Empty : string.Empty;
            var now = time.GetUtcNow().UtcDateTime;

            switch (type)
            {
                case "payment.captured" or "order.paid":
                    if (TryPayment(root, out var captured))
                    {
                        var outcome = await store.ApplyCapturedAsync(captured.OrderId, captured.Id, captured.Amount, now, cancellationToken);
                        log.LogInformation("Payment notification {Type}: {Outcome}", type, outcome);
                    }

                    break;
                case "payment.failed":
                    if (TryPayment(root, out var failed))
                    {
                        await store.ApplyFailedAsync(failed.OrderId, failed.Id, failed.Reason ?? "Payment failed", now, cancellationToken);
                    }

                    break;
                case "refund.processed" or "refund.failed":
                    if (TryRefund(root, out var refund))
                    {
                        await store.ApplyRefundResultAsync(refund.PaymentId, refund.Id, type == "refund.processed", now, cancellationToken);
                    }

                    break;
                default:
                    log.LogDebug("Payment notification of an unhandled type {Type} ignored", type);
                    break;
            }

            // Remembered only after it worked, so a failure above makes the provider send it again. Handling twice is harmless.
            await store.TryRecordEventAsync(Provider, string.IsNullOrWhiteSpace(eventId) ? "body-" + Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(body))) : eventId.Trim(), type, now, cancellationToken);
            return WebhookStatus.Accepted;
        }
        catch (JsonException)
        {
            // Signed by the provider but not what we expected: nothing sending it again will help.
            return WebhookStatus.Accepted;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            log.LogError(ex, "A payment notification could not be handled; the provider will send it again");
            return WebhookStatus.Retry;
        }
    }

    private sealed record PaymentFields(string Id, string OrderId, long Amount, string? Reason);

    private sealed record RefundFields(string Id, string PaymentId);

    private static bool TryPayment(JsonElement root, out PaymentFields fields)
    {
        fields = default!;
        if (!root.TryGetProperty("payload", out var payload) || !payload.TryGetProperty("payment", out var payment) || !payment.TryGetProperty("entity", out var entity))
        {
            return false;
        }

        var id = Text(entity, "id");
        var orderId = Text(entity, "order_id");
        if (id is null || orderId is null || !entity.TryGetProperty("amount", out var amount) || !amount.TryGetInt64(out var paise))
        {
            return false;
        }

        fields = new PaymentFields(id, orderId, paise, Text(entity, "error_description"));
        return true;
    }

    private static bool TryRefund(JsonElement root, out RefundFields fields)
    {
        fields = default!;
        if (!root.TryGetProperty("payload", out var payload) || !payload.TryGetProperty("refund", out var refund) || !refund.TryGetProperty("entity", out var entity))
        {
            return false;
        }

        var id = Text(entity, "id");
        var paymentId = Text(entity, "payment_id");
        if (id is null || paymentId is null)
        {
            return false;
        }

        fields = new RefundFields(id, paymentId);
        return true;
    }

    private static string? Text(JsonElement element, string name) => element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;
}
