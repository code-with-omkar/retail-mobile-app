using System.Globalization;
using System.Net;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using QuickCommerce.Application.Interfaces;
using QuickCommerce.Application.Services;

namespace QuickCommerce.Infrastructure.Payments;

/// <summary>
/// Razorpay, over its REST API. Card and UPI details never pass through here: the customer pays on Razorpay's own screen and we only hear the
/// result. The secrets are used to sign requests and check signatures and are never written to a log or into an error message.
/// </summary>
public sealed class RazorpayGateway : IPaymentGateway
{
    public const string BaseAddress = "https://api.razorpay.com/";

    private readonly HttpClient http;
    private readonly PaymentSettings settings;

    public RazorpayGateway(HttpClient http, PaymentSettings settings)
    {
        this.http = http;
        this.settings = settings;
        if (http.BaseAddress is null)
        {
            http.BaseAddress = new Uri(BaseAddress);
        }

        http.Timeout = TimeSpan.FromSeconds(20);
        var basic = Convert.ToBase64String(Encoding.UTF8.GetBytes($"{settings.KeyId}:{settings.KeySecret}"));
        http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Basic", basic);
    }

    public async Task<GatewayOrder> CreateOrderAsync(long amountPaise, string currency, string receipt, CancellationToken cancellationToken = default)
    {
        using var json = await SendAsync(HttpMethod.Post, "v1/orders", new { amount = amountPaise, currency, receipt = Trim(receipt, 40), payment_capture = 1 }, cancellationToken);
        var root = json.RootElement;
        return new GatewayOrder(Required(root, "id"), root.TryGetProperty("amount", out var amount) && amount.TryGetInt64(out var paise) ? paise : amountPaise);
    }

    public bool VerifyPaymentSignature(string providerOrderId, string providerPaymentId, string signature) =>
        SameHex(Hmac(settings.KeySecret, $"{providerOrderId}|{providerPaymentId}"), signature);

    // No webhook secret configured: nothing can be proven to come from the provider, so every notification is refused.
    public bool VerifyWebhookSignature(string body, string signature) => settings.WebhookSecret.Length > 0 && SameHex(Hmac(settings.WebhookSecret, body), signature);

    public async Task<GatewayRefund> RefundAsync(string providerPaymentId, long amountPaise, string receipt, CancellationToken cancellationToken = default)
    {
        try
        {
            using var json = await SendAsync(HttpMethod.Post, $"v1/payments/{Uri.EscapeDataString(providerPaymentId)}/refund", new { amount = amountPaise, speed = "normal", receipt = Trim(receipt, 40) }, cancellationToken);
            return new GatewayRefund(Required(json.RootElement, "id"), json.RootElement.TryGetProperty("status", out var status) ? status.GetString() ?? "pending" : "pending");
        }
        catch (PaymentGatewayException ex) when (!ex.Transient && ex.Message.Contains("fully refunded", StringComparison.OrdinalIgnoreCase))
        {
            // A refund we made earlier (and did not get to write down) has already gone through: the customer has their money.
            return new GatewayRefund("already-refunded", "processed");
        }
    }

    public async Task<GatewayPayment?> FetchPaymentAsync(string providerPaymentId, CancellationToken cancellationToken = default)
    {
        try
        {
            using var json = await SendAsync(HttpMethod.Get, $"v1/payments/{Uri.EscapeDataString(providerPaymentId)}", null, cancellationToken);
            return ReadPayment(json.RootElement);
        }
        catch (PaymentGatewayException ex) when (ex.Code == "NOT_FOUND")
        {
            return null;
        }
    }

    public async Task<IReadOnlyList<GatewayPayment>> FetchOrderPaymentsAsync(string providerOrderId, CancellationToken cancellationToken = default)
    {
        using var json = await SendAsync(HttpMethod.Get, $"v1/orders/{Uri.EscapeDataString(providerOrderId)}/payments", null, cancellationToken);
        return json.RootElement.TryGetProperty("items", out var items) && items.ValueKind == JsonValueKind.Array
            ? items.EnumerateArray().Select(ReadPayment).ToArray()
            : [];
    }

    private static GatewayPayment ReadPayment(JsonElement root) => new(
        Required(root, "id"),
        root.TryGetProperty("order_id", out var orderId) && orderId.ValueKind == JsonValueKind.String ? orderId.GetString()! : string.Empty,
        root.TryGetProperty("amount", out var amount) && amount.TryGetInt64(out var paise) ? paise : 0,
        root.TryGetProperty("status", out var status) && status.ValueKind == JsonValueKind.String ? status.GetString()! : "created",
        root.TryGetProperty("amount_refunded", out var refunded) && refunded.TryGetInt64(out var back) ? back : 0);

    private async Task<JsonDocument> SendAsync(HttpMethod method, string path, object? body, CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(method, path);
        if (body is not null)
        {
            request.Content = new StringContent(JsonSerializer.Serialize(body), Encoding.UTF8, "application/json");
        }

        HttpResponseMessage response;
        try
        {
            response = await http.SendAsync(request, cancellationToken);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException && !cancellationToken.IsCancellationRequested)
        {
            throw new PaymentGatewayException("The payment provider could not be reached.", "NETWORK", transient: true);
        }

        using (response)
        {
            var text = await response.Content.ReadAsStringAsync(cancellationToken);
            if (response.IsSuccessStatusCode)
            {
                return JsonDocument.Parse(text);
            }

            // What we keep of a refusal is the provider's code and short description, never the request, headers or keys.
            var code = response.StatusCode == HttpStatusCode.NotFound ? "NOT_FOUND" : null;
            var description = "The payment provider refused the request.";
            try
            {
                using var error = JsonDocument.Parse(text);
                if (error.RootElement.TryGetProperty("error", out var e))
                {
                    code ??= e.TryGetProperty("code", out var c) ? c.GetString() : null;
                    description = e.TryGetProperty("description", out var d) ? Trim(d.GetString() ?? description, 200) : description;
                }
            }
            catch (JsonException)
            {
                // not JSON: keep the generic text
            }

            var transient = (int)response.StatusCode >= 500 || response.StatusCode == HttpStatusCode.TooManyRequests;
            throw new PaymentGatewayException(description, code, transient);
        }
    }

    private static string Required(JsonElement root, string name) =>
        root.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String && !string.IsNullOrEmpty(value.GetString())
            ? value.GetString()!
            : throw new PaymentGatewayException($"The payment provider's answer had no {name}.", "BAD_ANSWER", transient: false);

    private static string Trim(string value, int max) => value.Length <= max ? value : value[..max];

    private static string Hmac(string secret, string text) =>
        Convert.ToHexString(HMACSHA256.HashData(Encoding.UTF8.GetBytes(secret), Encoding.UTF8.GetBytes(text))).ToLower(CultureInfo.InvariantCulture);

    /// <summary>Compares two hex strings without stopping at the first difference, so timing reveals nothing.</summary>
    private static bool SameHex(string expected, string given) =>
        CryptographicOperations.FixedTimeEquals(Encoding.ASCII.GetBytes(expected), Encoding.ASCII.GetBytes((given ?? string.Empty).Trim().ToLower(CultureInfo.InvariantCulture)));
}

/// <summary>Used while online payment is switched off: nothing can reach a provider.</summary>
public sealed class DisabledPaymentGateway : IPaymentGateway
{
    private static PaymentGatewayException Off() => new("Online payment is switched off.", "DISABLED", transient: false);

    public Task<GatewayOrder> CreateOrderAsync(long amountPaise, string currency, string receipt, CancellationToken cancellationToken = default) => throw Off();
    public bool VerifyPaymentSignature(string providerOrderId, string providerPaymentId, string signature) => false;
    public bool VerifyWebhookSignature(string body, string signature) => false;
    public Task<GatewayRefund> RefundAsync(string providerPaymentId, long amountPaise, string receipt, CancellationToken cancellationToken = default) => throw Off();
    public Task<GatewayPayment?> FetchPaymentAsync(string providerPaymentId, CancellationToken cancellationToken = default) => throw Off();
    public Task<IReadOnlyList<GatewayPayment>> FetchOrderPaymentsAsync(string providerOrderId, CancellationToken cancellationToken = default) => throw Off();
}
