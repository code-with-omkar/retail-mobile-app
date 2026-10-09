using System.Globalization;
using System.Net;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging.Abstractions;
using QuickCommerce.Api.Security;
using QuickCommerce.Application;
using QuickCommerce.Application.DTOs;
using QuickCommerce.Application.Interfaces;
using QuickCommerce.Application.Services;
using QuickCommerce.Application.Validators;
using QuickCommerce.Domain;
using Xunit;
using QuickCommerce.Infrastructure;
using QuickCommerce.Infrastructure.Payments;
using QuickCommerce.Infrastructure.Security;

namespace QuickCommerce.Tests;

/// <summary>A clock the test moves by hand.</summary>
public sealed class TestClock(DateTimeOffset start) : TimeProvider
{
    public DateTimeOffset Now { get; set; } = start;
    public override DateTimeOffset GetUtcNow() => Now;
    public void Advance(TimeSpan span) => Now += span;
}

/// <summary>
/// A stand-in for Razorpay's REST API, so the real <see cref="RazorpayGateway"/> (requests, signatures, error mapping) is what the tests run.
/// It keeps provider orders and payments, signs like Razorpay does, and fails when told to. The keys here are made up for tests.
/// </summary>
public sealed class FakeRazorpay : HttpMessageHandler
{
    public const string KeyId = "rzp_test_unitkey00001";
    public const string KeySecret = "unit-test-key-secret-0001";
    public const string WebhookSecret = "unit-test-webhook-secret-01";

    public sealed record Pay(string Id, string OrderId, long Amount, string Status, long Refunded);

    public List<(HttpMethod Method, string Path, string Body, string? Authorization)> Requests { get; } = [];
    public Dictionary<string, long> Orders { get; } = [];
    public Dictionary<string, Pay> Payments { get; } = [];

    /// <summary>The provider cannot be reached.</summary>
    public bool Down { get; set; }

    /// <summary>The next request is answered with this status and body (once).</summary>
    public (HttpStatusCode Status, string Body)? FailNext { get; set; }

    /// <summary>What a refund reports back: "processed" or "pending".</summary>
    public string RefundStatus { get; set; } = "processed";

    private int counter;

    public int Count(HttpMethod method, string startsWith) => Requests.Count(r => r.Method == method && r.Path.StartsWith(startsWith, StringComparison.Ordinal));

    /// <summary>What the customer's payment screen does: pays a provider order and returns the payment id.</summary>
    public string PayOrder(string providerOrderId, long? amount = null, string status = "captured")
    {
        var id = $"pay_Test{++counter:D5}";
        Payments[id] = new Pay(id, providerOrderId, amount ?? Orders[providerOrderId], status, 0);
        return id;
    }

    public static string Sign(string providerOrderId, string providerPaymentId) => Hex(HMACSHA256.HashData(Encoding.UTF8.GetBytes(KeySecret), Encoding.UTF8.GetBytes($"{providerOrderId}|{providerPaymentId}")));

    public static string SignWebhook(string body) => Hex(HMACSHA256.HashData(Encoding.UTF8.GetBytes(WebhookSecret), Encoding.UTF8.GetBytes(body)));

    private static string Hex(byte[] bytes) => Convert.ToHexString(bytes).ToLower(CultureInfo.InvariantCulture);

    public static string CapturedBody(string paymentId, string providerOrderId, long amount, string eventName = "payment.captured") =>
        JsonSerializer.Serialize(new { @event = eventName, payload = new { payment = new { entity = new { id = paymentId, order_id = providerOrderId, amount, status = "captured" } } } });

    public static string FailedBody(string paymentId, string providerOrderId, long amount) =>
        JsonSerializer.Serialize(new { @event = "payment.failed", payload = new { payment = new { entity = new { id = paymentId, order_id = providerOrderId, amount, status = "failed", error_description = "Payment failed at the bank" } } } });

    public static string RefundBody(string refundId, string paymentId, string eventName = "refund.processed") =>
        JsonSerializer.Serialize(new { @event = eventName, payload = new { refund = new { entity = new { id = refundId, payment_id = paymentId, status = eventName == "refund.processed" ? "processed" : "failed" } } } });

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var body = request.Content is null ? string.Empty : await request.Content.ReadAsStringAsync(cancellationToken);
        var path = request.RequestUri!.AbsolutePath.TrimStart('/');
        Requests.Add((request.Method, path, body, request.Headers.Authorization?.ToString()));
        if (Down)
        {
            throw new HttpRequestException("connection refused");
        }

        if (FailNext is { } failure)
        {
            FailNext = null;
            return Reply(failure.Status, failure.Body);
        }

        var parts = path.Split('/');
        if (request.Method == HttpMethod.Post && path == "v1/orders")
        {
            using var json = JsonDocument.Parse(body);
            var id = $"order_Test{++counter:D5}";
            var amount = json.RootElement.GetProperty("amount").GetInt64();
            Orders[id] = amount;
            return Reply(HttpStatusCode.OK, JsonSerializer.Serialize(new { id, amount, currency = "INR", status = "created" }));
        }

        if (request.Method == HttpMethod.Get && parts is ["v1", "payments", var paymentId])
        {
            return Payments.TryGetValue(paymentId, out var pay)
                ? Reply(HttpStatusCode.OK, Json(pay))
                : Reply(HttpStatusCode.BadRequest, """{"error":{"code":"BAD_REQUEST_ERROR","description":"The id provided does not exist"}}""");
        }

        if (request.Method == HttpMethod.Get && parts is ["v1", "orders", var orderId, "payments"])
        {
            return Reply(HttpStatusCode.OK, "{\"items\":[" + string.Join(",", Payments.Values.Where(p => p.OrderId == orderId).Select(Json)) + "]}");
        }

        if (request.Method == HttpMethod.Post && parts is ["v1", "payments", var refundedId, "refund"])
        {
            if (!Payments.TryGetValue(refundedId, out var pay))
            {
                return Reply(HttpStatusCode.NotFound, """{"error":{"code":"NOT_FOUND","description":"not found"}}""");
            }

            using var json = JsonDocument.Parse(body);
            var amount = json.RootElement.GetProperty("amount").GetInt64();
            if (pay.Refunded >= pay.Amount)
            {
                return Reply(HttpStatusCode.BadRequest, """{"error":{"code":"BAD_REQUEST_ERROR","description":"The payment has been fully refunded already"}}""");
            }

            Payments[refundedId] = pay with { Refunded = pay.Refunded + amount };
            return Reply(HttpStatusCode.OK, JsonSerializer.Serialize(new { id = $"rfnd_Test{++counter:D5}", payment_id = refundedId, amount, status = RefundStatus }));
        }

        return Reply(HttpStatusCode.NotFound, """{"error":{"code":"NOT_FOUND","description":"unknown route"}}""");
    }

    private static string Json(Pay pay) => JsonSerializer.Serialize(new { id = pay.Id, order_id = pay.OrderId, amount = pay.Amount, status = pay.Status, amount_refunded = pay.Refunded });

    private static HttpResponseMessage Reply(HttpStatusCode status, string body) => new(status) { Content = new StringContent(body, Encoding.UTF8, "application/json") };
}

/// <summary>Everything a payment test needs: the shop, the services, the fake provider and the clock.</summary>
public sealed class PaymentRig
{
    public required InMemoryCommerceStore Data { get; init; }
    public required FakeRazorpay Razorpay { get; init; }
    public required TestClock Clock { get; init; }
    public required PaymentSettings Settings { get; init; }
    public required RazorpayGateway Gateway { get; init; }
    public required CartService Cart { get; init; }
    public required CheckoutService Checkout { get; init; }
    public required CustomerOrderService Orders { get; init; }
    public required PaymentService Payments { get; init; }
    public required PaymentWebhookService Webhooks { get; init; }
    public required PaymentMaintenance Maintenance { get; init; }
    public required Store Store { get; init; }
    public required Product Tomato { get; init; }

    public int Stock => Data.StockOf(Store, Tomato).AvailableQuantity;
    public Customer Customer => Data.Customers.Single();
    public CheckoutRequest Where(string? method = PaymentMethods.Online) => new("12 Main Street", 19.07, 72.87, PaymentMethod: method);

    public static PaymentRig Create(bool enabled = true, Action<PaymentSettings>? tune = null, Func<IPaymentStore, IPaymentStore>? wrapStore = null)
    {
        var data = InMemoryCommerceStore.CreateSeeded();
        var settings = new PaymentSettings { Enabled = enabled, KeyId = FakeRazorpay.KeyId, KeySecret = FakeRazorpay.KeySecret, WebhookSecret = FakeRazorpay.WebhookSecret };
        tune?.Invoke(settings);
        var user = data.Users.Single();
        var currentUser = new HttpContextCurrentUser(new HttpContextAccessor
        {
            HttpContext = new DefaultHttpContext
            {
                User = new ClaimsPrincipal(new ClaimsIdentity(
                [
                    new Claim(ClaimTypes.NameIdentifier, user.ExternalSubject),
                    new Claim("organization_id", data.Organizations.Single().Id.ToString())
                ], "Test"))
            }
        });
        var resolver = new CurrentUserContextResolver(currentUser, data);
        var clock = new TestClock(new DateTimeOffset(2026, 10, 9, 12, 0, 0, TimeSpan.Zero));
        var razorpay = new FakeRazorpay();
        var gateway = new RazorpayGateway(new HttpClient(razorpay) { BaseAddress = new Uri(RazorpayGateway.BaseAddress) }, settings);
        IPaymentStore store = wrapStore is null ? data : wrapStore(data);
        var orders = new CustomerOrderService(data, resolver);
        return new PaymentRig
        {
            Data = data,
            Razorpay = razorpay,
            Clock = clock,
            Settings = settings,
            Gateway = gateway,
            Cart = new CartService(data, resolver, new AddCartItemRequestValidator(), new UpdateCartItemRequestValidator(), new PricingSettings { DeliveryFee = 25, HandlingFee = 5, FreeDeliveryThreshold = 199 }),
            Checkout = new CheckoutService(data, resolver, new CheckoutRequestValidator(), new PricingSettings { DeliveryFee = 25, HandlingFee = 5, FreeDeliveryThreshold = 199 }, null, new DeliveryEstimator(new DeliverySettings()), settings, clock),
            Orders = orders,
            Payments = new PaymentService(store, gateway, settings, resolver, orders, clock, NullLogger<PaymentService>.Instance),
            Webhooks = new PaymentWebhookService(store, gateway, clock, NullLogger<PaymentWebhookService>.Instance),
            Maintenance = new PaymentMaintenance(store, gateway, settings, clock, NullLogger<PaymentMaintenance>.Instance),
            Store = data.Stores.Single(item => item.Name == "Harbor Point Dark Store"),
            Tomato = data.Products.Single(item => item.Name == "Tomato")
        };
    }

    /// <summary>Puts tomatoes in the cart and checks out online; returns the order.</summary>
    public async Task<OrderResponse> PlaceOnlineAsync(int quantity = 2, string key = "online-key-0000001", string? method = PaymentMethods.Online)
    {
        await Cart.AddItemAsync(Store.Id, new AddCartItemRequest(Tomato.Id, quantity));
        var placed = await Checkout.CheckoutAsync(Store.Id, Where(method), key);
        Assert.Equal(CheckoutOperationStatus.Succeeded, placed.Status);
        return placed.Order!;
    }

    /// <summary>The customer starts paying and pays on the provider's screen. Returns what the app would then send to the server.</summary>
    public async Task<(StartPaymentResponse Start, string PaymentId, ConfirmPaymentRequest Confirm)> PayAsync(Guid orderId, long? amount = null, string status = "captured")
    {
        var started = await Payments.StartAsync(orderId);
        Assert.Equal(PaymentOperationStatus.Succeeded, started.Status);
        var id = Razorpay.PayOrder(started.Value!.ProviderOrderId, amount, status);
        return (started.Value, id, new ConfirmPaymentRequest(started.Value.ProviderOrderId, id, FakeRazorpay.Sign(started.Value.ProviderOrderId, id)));
    }

    public Order Row(Guid orderId) => Data.Orders.Single(order => order.Id == orderId);
    public Payment PaymentOf(Guid orderId) => Data.Payments.Single(payment => payment.OrderId == orderId);

    public Task<WebhookStatus> SendWebhookAsync(string body, string? signature = null, string? eventId = "evt_1") => Webhooks.HandleAsync(body, signature ?? FakeRazorpay.SignWebhook(body), eventId);
}
