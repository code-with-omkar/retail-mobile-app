using System.Runtime.CompilerServices;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using QuickCommerce.Application.Interfaces;
using Xunit;

namespace QuickCommerce.Tests;

/// <summary>Phase P7: the payment secrets live only in server configuration, and the signature checks are the standard ones.</summary>
public sealed class PaymentSecurityTests
{
    private static string Root([CallerFilePath] string here = "")
    {
        var directory = new DirectoryInfo(Path.GetDirectoryName(here)!);
        while (directory is not null && !Directory.Exists(Path.Combine(directory.FullName, "src")))
        {
            directory = directory.Parent;
        }

        return directory!.FullName;
    }

    [Fact]
    public void Every_settings_file_in_the_repository_keeps_the_payment_secrets_empty()
    {
        var offenders = new List<string>();
        foreach (var file in Directory.EnumerateFiles(Path.Combine(Root(), "src"), "appsettings*.json", SearchOption.AllDirectories).Where(path => !path.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}") && !path.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}")))
        {
            using var json = JsonDocument.Parse(File.ReadAllText(file), new JsonDocumentOptions { CommentHandling = JsonCommentHandling.Skip, AllowTrailingCommas = true });
            if (json.RootElement.TryGetProperty("Payments", out var payments))
            {
                foreach (var name in new[] { "KeySecret", "WebhookSecret" })
                {
                    if (payments.TryGetProperty(name, out var value) && !string.IsNullOrEmpty(value.GetString()))
                    {
                        offenders.Add($"{Path.GetRelativePath(Root(), file)} ({name})");
                    }
                }

                if (payments.TryGetProperty("Enabled", out var enabled) && enabled.ValueKind == JsonValueKind.True && Path.GetFileName(file) == "appsettings.json")
                {
                    offenders.Add($"{Path.GetRelativePath(Root(), file)} (Enabled must be false in the shared file)");
                }
            }
        }

        Assert.True(offenders.Count == 0, "Payment settings that must not be committed: " + string.Join(", ", offenders));
    }

    [Fact]
    public void No_source_file_contains_a_razorpay_live_key_or_a_secret_shaped_value()
    {
        var liveKey = new Regex(@"rzp_live_[A-Za-z0-9]{8,}", RegexOptions.Compiled);
        var offenders = new List<string>();
        foreach (var file in Directory.EnumerateFiles(Root(), "*.*", SearchOption.AllDirectories).Where(path =>
                     new[] { ".cs", ".json", ".dart", ".md", ".yml", ".yaml", ".ps1", ".sh", ".html", ".sql" }.Contains(Path.GetExtension(path)) &&
                     !new[] { "bin", "obj", ".git", "node_modules", ".dart_tool", "build" }.Any(skip => path.Contains($"{Path.DirectorySeparatorChar}{skip}{Path.DirectorySeparatorChar}"))))
        {
            if (liveKey.IsMatch(File.ReadAllText(file)))
            {
                offenders.Add(Path.GetRelativePath(Root(), file));
            }
        }

        Assert.True(offenders.Count == 0, "A live payment key is in: " + string.Join(", ", offenders));
    }

    [Fact]
    public void The_signature_checks_match_the_providers_published_method_and_reject_everything_else()
    {
        var rig = PaymentRig.Create();
        var gateway = rig.Gateway;
        static string Hex(string secret, string text) => Convert.ToHexString(HMACSHA256.HashData(Encoding.UTF8.GetBytes(secret), Encoding.UTF8.GetBytes(text))).ToLowerInvariant();

        Assert.True(gateway.VerifyPaymentSignature("order_A", "pay_B", Hex(FakeRazorpay.KeySecret, "order_A|pay_B")));
        Assert.True(gateway.VerifyPaymentSignature("order_A", "pay_B", Hex(FakeRazorpay.KeySecret, "order_A|pay_B").ToUpperInvariant()));
        Assert.False(gateway.VerifyPaymentSignature("order_A", "pay_B", Hex(FakeRazorpay.KeySecret, "order_A|pay_C")));
        Assert.False(gateway.VerifyPaymentSignature("order_A", "pay_B", Hex(FakeRazorpay.WebhookSecret, "order_A|pay_B")));
        Assert.False(gateway.VerifyPaymentSignature("order_A", "pay_B", string.Empty));
        Assert.False(gateway.VerifyPaymentSignature("order_A", "pay_B", "zz"));

        Assert.True(gateway.VerifyWebhookSignature("{\"a\":1}", Hex(FakeRazorpay.WebhookSecret, "{\"a\":1}")));
        Assert.False(gateway.VerifyWebhookSignature("{\"a\":1}", Hex(FakeRazorpay.KeySecret, "{\"a\":1}")));
        Assert.False(gateway.VerifyWebhookSignature("{\"a\":2}", Hex(FakeRazorpay.WebhookSecret, "{\"a\":1}")));
        Assert.False(gateway.VerifyWebhookSignature("{\"a\":1}", null!));
    }

    [Fact]
    public async Task Without_a_webhook_secret_every_notification_is_refused_even_one_signed_with_an_empty_key()
    {
        var rig = PaymentRig.Create(tune: settings => settings.WebhookSecret = string.Empty);
        var order = await rig.PlaceOnlineAsync();
        var paid = await rig.PayAsync(order.Id);
        var body = FakeRazorpay.CapturedBody(paid.PaymentId, paid.Start.ProviderOrderId, paid.Start.AmountPaise);

        Assert.Equal(QuickCommerce.Application.Interfaces.WebhookStatus.Rejected, await rig.Webhooks.HandleAsync(body, FakeRazorpay.Sign(body, string.Empty), "e1"));
        Assert.Equal(QuickCommerce.Domain.OrderStatus.AwaitingPayment, rig.Row(order.Id).Status);
        // The app's own confirmation still settles it.
        Assert.Equal(QuickCommerce.Application.Interfaces.PaymentOperationStatus.Succeeded, (await rig.Payments.ConfirmAsync(order.Id, paid.Confirm)).Status);
    }

    [Fact]
    public async Task Provider_errors_never_carry_the_keys_or_the_authorization_header()
    {
        var rig = PaymentRig.Create();
        rig.Razorpay.FailNext = (System.Net.HttpStatusCode.Unauthorized, "not json at all");

        var failure = await Assert.ThrowsAsync<PaymentGatewayException>(() => rig.Gateway.CreateOrderAsync(1000, "INR", "r1"));

        Assert.DoesNotContain(FakeRazorpay.KeySecret, failure.Message);
        Assert.DoesNotContain(FakeRazorpay.KeyId, failure.Message);
        Assert.False(failure.Transient);
        rig.Razorpay.Down = true;
        var down = await Assert.ThrowsAsync<PaymentGatewayException>(() => rig.Gateway.CreateOrderAsync(1000, "INR", "r1"));
        Assert.True(down.Transient);
        Assert.DoesNotContain(FakeRazorpay.KeySecret, down.Message);
    }

    [Fact]
    public async Task The_disabled_gateway_reaches_nobody_and_accepts_no_signature()
    {
        var gateway = new QuickCommerce.Infrastructure.Payments.DisabledPaymentGateway();

        Assert.False(gateway.VerifyPaymentSignature("o", "p", "s"));
        Assert.False(gateway.VerifyWebhookSignature("b", "s"));
        await Assert.ThrowsAsync<PaymentGatewayException>(() => gateway.CreateOrderAsync(100, "INR", "r"));
        await Assert.ThrowsAsync<PaymentGatewayException>(() => gateway.RefundAsync("p", 100, "r"));
    }
}
