using System.Net;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc;
using QuickCommerce.Api.Controllers;
using QuickCommerce.Application.DTOs;
using QuickCommerce.Application.Interfaces;
using QuickCommerce.Application.Services;
using QuickCommerce.Domain;
using Xunit;

namespace QuickCommerce.Tests;

/// <summary>
/// Phase P7: online payment. Hold, pay, confirm, the provider's notifications, expiry and refunds, run against the real Razorpay gateway
/// code talking to a stand-in for Razorpay. The SQL Server behaviour (two things at once) is in PaymentEfIntegrationTests.
/// </summary>
public sealed class PaymentServiceTests
{
    // ---------------- ordering online ----------------

    [Fact]
    public async Task An_online_order_waits_for_payment_holds_its_items_and_records_the_amount_the_server_worked_out()
    {
        var rig = PaymentRig.Create();
        var stock = rig.Stock;

        var order = await rig.PlaceOnlineAsync(quantity: 3);

        Assert.Equal((OrderStatus.AwaitingPayment, PaymentState.Created, PaymentMethods.Online), (order.Status, order.PaymentStatus, order.PaymentMethod));
        Assert.Equal(rig.Clock.Now.UtcDateTime.AddMinutes(15), order.PaymentExpiresAt);
        Assert.Equal(stock - 3, rig.Stock);
        var payment = rig.PaymentOf(order.Id);
        Assert.Equal((long)(order.TotalAmount * 100), payment.AmountPaise);
        Assert.Equal((PaymentState.Created, "INR", 0), (payment.Status, payment.Currency, payment.Attempts));
        Assert.Equal([OrderStatus.AwaitingPayment], order.StatusHistory.Select(history => history.Status));
    }

    [Fact]
    public async Task Cash_on_delivery_is_unchanged_and_needs_no_payment()
    {
        var rig = PaymentRig.Create();

        var order = await rig.PlaceOnlineAsync(method: PaymentMethods.CashOnDelivery);

        Assert.Equal((OrderStatus.Pending, PaymentState.NotRequired, null), (order.Status, order.PaymentStatus, order.PaymentExpiresAt));
        Assert.Empty(rig.Data.Payments);
    }

    [Fact]
    public async Task Without_a_payment_method_it_is_cash_on_delivery_as_before()
    {
        var rig = PaymentRig.Create();
        await rig.Cart.AddItemAsync(rig.Store.Id, new AddCartItemRequest(rig.Tomato.Id, 1));

        var placed = await rig.Checkout.CheckoutAsync(rig.Store.Id, new CheckoutRequest("12 Main Street", 19.07, 72.87), "default-key-000001");

        Assert.Equal((OrderStatus.Pending, PaymentMethods.CashOnDelivery), (placed.Order!.Status, placed.Order.PaymentMethod));
    }

    [Fact]
    public async Task While_online_payment_is_off_an_online_order_is_refused_and_nothing_is_taken()
    {
        var rig = PaymentRig.Create(enabled: false);
        await rig.Cart.AddItemAsync(rig.Store.Id, new AddCartItemRequest(rig.Tomato.Id, 2));
        var stock = rig.Stock;

        var result = await rig.Checkout.CheckoutAsync(rig.Store.Id, rig.Where(), "off-key-0000000001");

        Assert.Equal((CheckoutOperationStatus.Conflict, CheckoutReasons.PaymentsUnavailable), (result.Status, result.Reason));
        Assert.Equal(stock, rig.Stock);
        Assert.Empty(rig.Data.Orders);
    }

    [Theory]
    [InlineData("Bitcoin")]
    [InlineData("online")]
    [InlineData("")]
    public async Task An_unknown_payment_method_is_refused(string method)
    {
        var rig = PaymentRig.Create();
        await rig.Cart.AddItemAsync(rig.Store.Id, new AddCartItemRequest(rig.Tomato.Id, 1));

        var result = await rig.Checkout.CheckoutAsync(rig.Store.Id, rig.Where(method), "method-key-0000001");

        Assert.Equal(CheckoutOperationStatus.InvalidRequest, result.Status);
        Assert.Empty(rig.Data.Orders);
    }

    [Fact]
    public async Task The_same_key_returns_the_same_online_order_but_not_for_another_payment_method()
    {
        var rig = PaymentRig.Create();
        var first = await rig.PlaceOnlineAsync(key: "same-key-00000001");

        var again = await rig.Checkout.CheckoutAsync(rig.Store.Id, rig.Where(), "same-key-00000001");
        var other = await rig.Checkout.CheckoutAsync(rig.Store.Id, rig.Where(PaymentMethods.CashOnDelivery), "same-key-00000001");

        Assert.True(again.Replayed);
        Assert.Equal(first.Id, again.Order!.Id);
        Assert.Equal((CheckoutOperationStatus.Conflict, CheckoutReasons.IdempotencyKeyReused), (other.Status, other.Reason));
        Assert.Single(rig.Data.Orders);
        Assert.Single(rig.Data.Payments);
    }

    [Fact]
    public async Task The_shop_does_not_see_an_order_until_it_is_paid()
    {
        var rig = PaymentRig.Create();
        var organization = rig.Data.Organizations.Single().Id;
        var storeIds = new HashSet<Guid> { rig.Store.Id };
        var order = await rig.PlaceOnlineAsync();

        Assert.Empty(await rig.Data.GetScopedOrdersAsync(organization, storeIds, true));
        Assert.Empty(await rig.Data.GetScopedOrderSummariesAsync(organization, storeIds, true));
        Assert.Empty(await rig.Data.GetOrdersAsync());
        Assert.Null(await rig.Data.GetOrderAsync(order.Id));
        Assert.Equal(0, (await rig.Data.GetAdminDashboardAsync(organization, storeIds, true)).TotalOrders);
        Assert.Single((await rig.Orders.GetHistoryAsync())!);

        var paid = await rig.PayAsync(order.Id);
        await rig.Payments.ConfirmAsync(order.Id, paid.Confirm);

        Assert.Single(await rig.Data.GetScopedOrdersAsync(organization, storeIds, true));
        Assert.Single(await rig.Data.GetScopedOrderSummariesAsync(organization, storeIds, true));
        Assert.NotNull(await rig.Data.GetOrderAsync(order.Id));
    }

    [Fact]
    public async Task The_shop_cannot_accept_an_unpaid_order()
    {
        var rig = PaymentRig.Create();
        var order = await rig.PlaceOnlineAsync();

        var result = await rig.Data.TryTransitionOrderAsync(order.Id, rig.Data.Organizations.Single().Id, null, OrderStatus.Accepted);

        Assert.NotEqual(OrderLifecycleStatus.Succeeded, result.Status);
        Assert.Equal(OrderStatus.AwaitingPayment, rig.Row(order.Id).Status);
    }

    // ---------------- starting the payment ----------------

    [Fact]
    public async Task Starting_gives_the_payment_screen_what_it_needs_and_no_secret()
    {
        var rig = PaymentRig.Create();
        var order = await rig.PlaceOnlineAsync();

        var started = await rig.Payments.StartAsync(order.Id);

        Assert.Equal(PaymentOperationStatus.Succeeded, started.Status);
        var start = started.Value!;
        Assert.Equal(("Razorpay", FakeRazorpay.KeyId, "INR", order.OrderNumber), (start.Provider, start.KeyId, start.Currency, start.OrderNumber));
        Assert.Equal(rig.PaymentOf(order.Id).AmountPaise, start.AmountPaise);
        Assert.Equal(rig.Razorpay.Orders[start.ProviderOrderId], start.AmountPaise);
        Assert.Equal(order.PaymentExpiresAt, start.ExpiresAt);
        var serialized = JsonSerializer.Serialize(started);
        Assert.DoesNotContain(FakeRazorpay.KeySecret, serialized);
        Assert.DoesNotContain(FakeRazorpay.WebhookSecret, serialized);
    }

    [Fact]
    public async Task The_amount_sent_to_the_provider_is_the_server_s_own_total_in_paise()
    {
        var rig = PaymentRig.Create();
        var order = await rig.PlaceOnlineAsync(quantity: 4);

        await rig.Payments.StartAsync(order.Id);

        var sent = rig.Razorpay.Requests.Single(request => request.Path == "v1/orders");
        using var json = JsonDocument.Parse(sent.Body);
        Assert.Equal((long)(order.TotalAmount * 100), json.RootElement.GetProperty("amount").GetInt64());
        Assert.Equal("INR", json.RootElement.GetProperty("currency").GetString());
        Assert.Equal(order.OrderNumber[..Math.Min(40, order.OrderNumber.Length)], json.RootElement.GetProperty("receipt").GetString());
        Assert.StartsWith("Basic ", sent.Authorization);
    }

    [Fact]
    public async Task Trying_again_reuses_the_provider_order_so_the_customer_cannot_be_charged_twice_for_one_order()
    {
        var rig = PaymentRig.Create();
        var order = await rig.PlaceOnlineAsync();

        var first = (await rig.Payments.StartAsync(order.Id)).Value!;
        var second = (await rig.Payments.StartAsync(order.Id)).Value!;

        Assert.Equal(first.ProviderOrderId, second.ProviderOrderId);
        Assert.Equal(1, rig.Razorpay.Count(HttpMethod.Post, "v1/orders"));
        Assert.Equal(2, rig.PaymentOf(order.Id).Attempts);
    }

    [Fact]
    public async Task Starting_after_a_failed_attempt_puts_the_payment_back_in_progress()
    {
        var rig = PaymentRig.Create();
        var order = await rig.PlaceOnlineAsync();
        var start = (await rig.Payments.StartAsync(order.Id)).Value!;
        await rig.SendWebhookAsync(FakeRazorpay.FailedBody("pay_failed_1", start.ProviderOrderId, start.AmountPaise));
        Assert.Equal(PaymentState.Failed, rig.PaymentOf(order.Id).Status);

        var again = await rig.Payments.StartAsync(order.Id);

        Assert.Equal(PaymentOperationStatus.Succeeded, again.Status);
        Assert.Equal((PaymentState.Created, PaymentState.Created), (rig.PaymentOf(order.Id).Status, rig.Row(order.Id).PaymentStatus));
    }

    [Fact]
    public async Task If_the_provider_cannot_be_reached_nothing_is_attached_and_trying_again_works()
    {
        var rig = PaymentRig.Create();
        var order = await rig.PlaceOnlineAsync();
        rig.Razorpay.Down = true;

        var down = await rig.Payments.StartAsync(order.Id);
        rig.Razorpay.Down = false;
        var later = await rig.Payments.StartAsync(order.Id);

        Assert.Equal((PaymentOperationStatus.Conflict, PaymentReasons.ProviderUnavailable), (down.Status, down.Reason));
        Assert.Equal(PaymentOperationStatus.Succeeded, later.Status);
        Assert.Equal(1, rig.Razorpay.Orders.Count);
    }

    [Fact]
    public async Task A_provider_refusal_is_reported_without_its_details()
    {
        var rig = PaymentRig.Create();
        var order = await rig.PlaceOnlineAsync();
        rig.Razorpay.FailNext = (HttpStatusCode.BadRequest, """{"error":{"code":"BAD_REQUEST_ERROR","description":"Authentication failed for key rzp_test_unitkey00001"}}""");

        var result = await rig.Payments.StartAsync(order.Id);

        Assert.Equal(PaymentReasons.ProviderUnavailable, result.Reason);
        Assert.DoesNotContain(FakeRazorpay.KeyId, result.Message);
    }

    [Fact]
    public async Task Starting_is_refused_with_a_reason_when_it_cannot_make_sense()
    {
        var rig = PaymentRig.Create();
        var cod = await rig.PlaceOnlineAsync(method: PaymentMethods.CashOnDelivery, key: "cod-key-00000001");
        var online = await rig.PlaceOnlineAsync(key: "online-key-0000002");

        Assert.Equal(PaymentReasons.NotAnOnlineOrder, (await rig.Payments.StartAsync(cod.Id)).Reason);
        Assert.Equal(PaymentOperationStatus.NotFound, (await rig.Payments.StartAsync(Guid.NewGuid())).Status);

        rig.Clock.Advance(TimeSpan.FromMinutes(16));
        Assert.Equal(PaymentReasons.HoldExpired, (await rig.Payments.StartAsync(online.Id)).Reason);
        await rig.Maintenance.ReleaseExpiredHoldsAsync();
        Assert.Equal(PaymentReasons.HoldExpired, (await rig.Payments.StartAsync(online.Id)).Reason);
    }

    [Fact]
    public async Task A_paid_order_cannot_be_paid_again()
    {
        var rig = PaymentRig.Create();
        var order = await rig.PlaceOnlineAsync();
        var paid = await rig.PayAsync(order.Id);
        await rig.Payments.ConfirmAsync(order.Id, paid.Confirm);

        var result = await rig.Payments.StartAsync(order.Id);

        Assert.Equal(PaymentReasons.AlreadyPaid, result.Reason);
        Assert.Equal(1, rig.Razorpay.Orders.Count);
    }

    [Fact]
    public async Task Another_customers_order_is_not_found_and_while_payments_are_off_nothing_starts()
    {
        var rig = PaymentRig.Create();
        var order = await rig.PlaceOnlineAsync();
        var theirs = new Order { OrderNumber = "ORD-THEIRS-1", UserId = Guid.NewGuid(), StoreId = rig.Store.Id, DeliveryAddress = "x", TotalAmount = 10, PaymentMethod = PaymentMethods.Online, Status = OrderStatus.AwaitingPayment, PaymentExpiresAt = rig.Clock.Now.UtcDateTime.AddMinutes(5) };
        rig.Data.Orders.Add(theirs);
        rig.Data.Payments.Add(new Payment { OrderId = theirs.Id, AmountPaise = 1000 });

        Assert.Equal(PaymentOperationStatus.NotFound, (await rig.Payments.StartAsync(theirs.Id)).Status);

        var off = PaymentRig.Create(enabled: false);
        Assert.Equal(PaymentReasons.PaymentsUnavailable, (await off.Payments.StartAsync(order.Id)).Reason);
        Assert.Equal(PaymentReasons.PaymentsUnavailable, (await off.Payments.ConfirmAsync(order.Id, new ConfirmPaymentRequest("o", "p", "s"))).Reason);
    }

    // ---------------- confirming ----------------

    [Fact]
    public async Task A_genuine_payment_makes_the_order_a_pending_order_the_shop_can_see_and_tells_the_customer()
    {
        var rig = PaymentRig.Create();
        var order = await rig.PlaceOnlineAsync();
        var paid = await rig.PayAsync(order.Id);

        var result = await rig.Payments.ConfirmAsync(order.Id, paid.Confirm);

        Assert.Equal(PaymentOperationStatus.Succeeded, result.Status);
        Assert.Equal((OrderStatus.Pending, PaymentState.Paid, (DateTime?)null), (result.Value!.Status, result.Value.PaymentStatus, result.Value.PaymentExpiresAt));
        Assert.Equal([OrderStatus.AwaitingPayment, OrderStatus.Pending], result.Value.StatusHistory.Select(history => history.Status));
        var payment = rig.PaymentOf(order.Id);
        Assert.Equal((PaymentState.Paid, paid.PaymentId), (payment.Status, payment.ProviderPaymentId));
        var note = Assert.Single(rig.Data.Notifications);
        Assert.Equal(("Payment received", order.Id), (note.Title, note.OrderId));
    }

    [Fact]
    public async Task Confirming_twice_changes_nothing_the_second_time()
    {
        var rig = PaymentRig.Create();
        var order = await rig.PlaceOnlineAsync();
        var paid = await rig.PayAsync(order.Id);

        var first = await rig.Payments.ConfirmAsync(order.Id, paid.Confirm);
        var second = await rig.Payments.ConfirmAsync(order.Id, paid.Confirm);

        Assert.Equal(PaymentOperationStatus.Succeeded, second.Status);
        Assert.Equal(first.Value!.StatusHistory.Count, second.Value!.StatusHistory.Count);
        Assert.Single(rig.Data.Notifications);
        Assert.Equal(2, rig.Row(order.Id).StatusHistory.Count);
    }

    [Fact]
    public async Task A_result_with_a_wrong_signature_is_refused_and_changes_nothing()
    {
        var rig = PaymentRig.Create();
        var order = await rig.PlaceOnlineAsync();
        var paid = await rig.PayAsync(order.Id);

        var result = await rig.Payments.ConfirmAsync(order.Id, paid.Confirm with { Signature = FakeRazorpay.Sign(paid.Start.ProviderOrderId, "pay_someone_else") });

        Assert.Equal((PaymentOperationStatus.Conflict, PaymentReasons.SignatureInvalid), (result.Status, result.Reason));
        Assert.Equal((OrderStatus.AwaitingPayment, PaymentState.Created), (rig.Row(order.Id).Status, rig.PaymentOf(order.Id).Status));
        Assert.Empty(rig.Data.Notifications);
    }

    [Theory]
    [InlineData("")]
    [InlineData("not-a-signature")]
    [InlineData("0000000000000000000000000000000000000000000000000000000000000000")]
    public async Task Made_up_signatures_never_pass(string signature)
    {
        var rig = PaymentRig.Create();
        var order = await rig.PlaceOnlineAsync();
        var paid = await rig.PayAsync(order.Id);

        var result = await rig.Payments.ConfirmAsync(order.Id, paid.Confirm with { Signature = signature });

        Assert.NotEqual(PaymentOperationStatus.Succeeded, result.Status);
        Assert.Equal(OrderStatus.AwaitingPayment, rig.Row(order.Id).Status);
    }

    [Fact]
    public async Task A_payment_for_another_provider_order_is_refused()
    {
        var rig = PaymentRig.Create();
        var order = await rig.PlaceOnlineAsync(key: "order-key-0000001");
        var other = await rig.PlaceOnlineAsync(key: "order-key-0000002");
        var paidOther = await rig.PayAsync(other.Id);

        var result = await rig.Payments.ConfirmAsync(order.Id, paidOther.Confirm);

        Assert.Equal(PaymentReasons.Mismatch, result.Reason);
        Assert.Equal(OrderStatus.AwaitingPayment, rig.Row(order.Id).Status);
    }

    [Fact]
    public async Task What_counts_is_what_the_provider_says_was_paid_so_a_smaller_payment_is_refunded_not_accepted()
    {
        var rig = PaymentRig.Create();
        var order = await rig.PlaceOnlineAsync();
        var start = await rig.Payments.StartAsync(order.Id);
        var short_ = rig.Razorpay.PayOrder(start.Value!.ProviderOrderId, amount: rig.PaymentOf(order.Id).AmountPaise - 100);

        var result = await rig.Payments.ConfirmAsync(order.Id, new ConfirmPaymentRequest(start.Value.ProviderOrderId, short_, FakeRazorpay.Sign(start.Value.ProviderOrderId, short_)));

        Assert.Equal((PaymentOperationStatus.Conflict, PaymentReasons.AmountMismatch), (result.Status, result.Reason));
        Assert.Equal(OrderStatus.AwaitingPayment, rig.Row(order.Id).Status);
        Assert.Equal(PaymentState.Refunding, rig.PaymentOf(order.Id).Status);
        await rig.Maintenance.StartRefundsAsync();
        Assert.Equal(1, rig.Razorpay.Count(HttpMethod.Post, "v1/payments/"));
    }

    [Fact]
    public async Task A_payment_the_provider_has_not_captured_yet_is_not_treated_as_paid()
    {
        var rig = PaymentRig.Create();
        var order = await rig.PlaceOnlineAsync();
        var pending = await rig.PayAsync(order.Id, status: "authorized");

        var result = await rig.Payments.ConfirmAsync(order.Id, pending.Confirm);

        Assert.Equal(PaymentReasons.NotCaptured, result.Reason);
        Assert.Equal(OrderStatus.AwaitingPayment, rig.Row(order.Id).Status);
    }

    [Fact]
    public async Task If_the_provider_cannot_be_asked_the_customer_is_told_it_will_be_confirmed_and_the_notification_then_settles_it()
    {
        var rig = PaymentRig.Create();
        var order = await rig.PlaceOnlineAsync();
        var paid = await rig.PayAsync(order.Id);
        rig.Razorpay.Down = true;

        var result = await rig.Payments.ConfirmAsync(order.Id, paid.Confirm);
        rig.Razorpay.Down = false;
        await rig.SendWebhookAsync(FakeRazorpay.CapturedBody(paid.PaymentId, paid.Start.ProviderOrderId, paid.Start.AmountPaise));

        Assert.Equal(PaymentReasons.ProviderUnavailable, result.Reason);
        Assert.Equal((OrderStatus.Pending, PaymentState.Paid), (rig.Row(order.Id).Status, rig.PaymentOf(order.Id).Status));
    }

    [Fact]
    public async Task A_payment_that_arrives_after_the_hold_ran_out_is_refunded_and_the_cancelled_order_stays_cancelled()
    {
        var rig = PaymentRig.Create();
        var order = await rig.PlaceOnlineAsync();
        var paid = await rig.PayAsync(order.Id);
        rig.Clock.Advance(TimeSpan.FromMinutes(16));
        await rig.Maintenance.ReleaseExpiredHoldsAsync();

        var result = await rig.Payments.ConfirmAsync(order.Id, paid.Confirm);

        Assert.Equal((PaymentOperationStatus.Conflict, PaymentReasons.HoldExpired), (result.Status, result.Reason));
        Assert.Equal((OrderStatus.Cancelled, PaymentState.Refunding), (rig.Row(order.Id).Status, rig.PaymentOf(order.Id).Status));
        Assert.Contains(rig.Data.Notifications, note => note.Title == "Payment will be refunded");
    }

    [Fact]
    public async Task Bad_requests_and_other_customers_get_the_right_answer()
    {
        var rig = PaymentRig.Create();
        var order = await rig.PlaceOnlineAsync();
        var paid = await rig.PayAsync(order.Id);

        Assert.Equal(PaymentOperationStatus.InvalidRequest, (await rig.Payments.ConfirmAsync(order.Id, paid.Confirm with { ProviderPaymentId = " " })).Status);
        Assert.Equal(PaymentOperationStatus.InvalidRequest, (await rig.Payments.ConfirmAsync(order.Id, paid.Confirm with { Signature = new string('a', 300) })).Status);
        Assert.Equal(PaymentOperationStatus.NotFound, (await rig.Payments.ConfirmAsync(Guid.NewGuid(), paid.Confirm)).Status);
    }

    // ---------------- the provider's notifications ----------------

    [Fact]
    public async Task A_payment_notification_makes_the_order_right_even_if_the_app_never_reported()
    {
        var rig = PaymentRig.Create();
        var order = await rig.PlaceOnlineAsync();
        var paid = await rig.PayAsync(order.Id);

        var status = await rig.SendWebhookAsync(FakeRazorpay.CapturedBody(paid.PaymentId, paid.Start.ProviderOrderId, paid.Start.AmountPaise));

        Assert.Equal(WebhookStatus.Accepted, status);
        Assert.Equal((OrderStatus.Pending, PaymentState.Paid), (rig.Row(order.Id).Status, rig.PaymentOf(order.Id).Status));
        Assert.Single(rig.Data.PaymentEvents);
    }

    [Fact]
    public async Task The_same_notification_twice_and_the_app_s_report_after_it_do_nothing_more()
    {
        var rig = PaymentRig.Create();
        var order = await rig.PlaceOnlineAsync();
        var paid = await rig.PayAsync(order.Id);
        var body = FakeRazorpay.CapturedBody(paid.PaymentId, paid.Start.ProviderOrderId, paid.Start.AmountPaise);

        await rig.SendWebhookAsync(body, eventId: "evt_same");
        await rig.SendWebhookAsync(body, eventId: "evt_same");
        await rig.SendWebhookAsync(FakeRazorpay.CapturedBody(paid.PaymentId, paid.Start.ProviderOrderId, paid.Start.AmountPaise, "order.paid"), eventId: "evt_other");
        var app = await rig.Payments.ConfirmAsync(order.Id, paid.Confirm);

        Assert.Equal(PaymentOperationStatus.Succeeded, app.Status);
        Assert.Equal(2, rig.Row(order.Id).StatusHistory.Count);
        Assert.Single(rig.Data.Notifications);
        Assert.Equal(2, rig.Data.PaymentEvents.Count);
    }

    [Fact]
    public async Task A_notification_without_a_valid_signature_is_refused_and_changes_nothing()
    {
        var rig = PaymentRig.Create();
        var order = await rig.PlaceOnlineAsync();
        var paid = await rig.PayAsync(order.Id);
        var body = FakeRazorpay.CapturedBody(paid.PaymentId, paid.Start.ProviderOrderId, paid.Start.AmountPaise);

        Assert.Equal(WebhookStatus.Rejected, await rig.Webhooks.HandleAsync(body, null, "e1"));
        Assert.Equal(WebhookStatus.Rejected, await rig.Webhooks.HandleAsync(body, "", "e1"));
        Assert.Equal(WebhookStatus.Rejected, await rig.Webhooks.HandleAsync(body, "abc123", "e1"));
        Assert.Equal(WebhookStatus.Rejected, await rig.Webhooks.HandleAsync(body + " ", FakeRazorpay.SignWebhook(body), "e1"));
        Assert.Equal(WebhookStatus.Rejected, await rig.Webhooks.HandleAsync(string.Empty, FakeRazorpay.SignWebhook(string.Empty), "e1"));

        Assert.Equal(OrderStatus.AwaitingPayment, rig.Row(order.Id).Status);
        Assert.Empty(rig.Data.PaymentEvents);
    }

    [Fact]
    public async Task A_notification_signed_with_the_key_secret_instead_of_the_webhook_secret_is_refused()
    {
        var rig = PaymentRig.Create();
        var order = await rig.PlaceOnlineAsync();
        var paid = await rig.PayAsync(order.Id);
        var body = FakeRazorpay.CapturedBody(paid.PaymentId, paid.Start.ProviderOrderId, paid.Start.AmountPaise);

        var status = await rig.Webhooks.HandleAsync(body, FakeRazorpay.Sign(body, string.Empty), "e1");

        Assert.Equal(WebhookStatus.Rejected, status);
    }

    [Fact]
    public async Task A_failed_attempt_leaves_the_order_waiting_so_the_customer_can_try_again()
    {
        var rig = PaymentRig.Create();
        var order = await rig.PlaceOnlineAsync();
        var start = (await rig.Payments.StartAsync(order.Id)).Value!;

        await rig.SendWebhookAsync(FakeRazorpay.FailedBody("pay_failed_1", start.ProviderOrderId, start.AmountPaise));

        Assert.Equal((OrderStatus.AwaitingPayment, PaymentState.Failed), (rig.Row(order.Id).Status, rig.Row(order.Id).PaymentStatus));
        Assert.Equal("Payment failed at the bank", rig.PaymentOf(order.Id).FailureReason);
        var paid = await rig.PayAsync(order.Id);
        Assert.Equal(PaymentOperationStatus.Succeeded, (await rig.Payments.ConfirmAsync(order.Id, paid.Confirm)).Status);
    }

    [Fact]
    public async Task A_failure_notice_arriving_after_the_payment_succeeded_never_undoes_it()
    {
        var rig = PaymentRig.Create();
        var order = await rig.PlaceOnlineAsync();
        var paid = await rig.PayAsync(order.Id);
        await rig.Payments.ConfirmAsync(order.Id, paid.Confirm);

        await rig.SendWebhookAsync(FakeRazorpay.FailedBody("pay_old_attempt", paid.Start.ProviderOrderId, paid.Start.AmountPaise));

        Assert.Equal((OrderStatus.Pending, PaymentState.Paid), (rig.Row(order.Id).Status, rig.PaymentOf(order.Id).Status));
    }

    [Fact]
    public async Task Notifications_that_are_unknown_broken_or_about_nothing_we_have_are_accepted_and_ignored()
    {
        var rig = PaymentRig.Create();
        var order = await rig.PlaceOnlineAsync();

        Assert.Equal(WebhookStatus.Accepted, await rig.SendWebhookAsync("""{"event":"subscription.charged","payload":{}}"""));
        Assert.Equal(WebhookStatus.Accepted, await rig.SendWebhookAsync("this is not json"));
        Assert.Equal(WebhookStatus.Accepted, await rig.SendWebhookAsync("""{"event":"payment.captured"}"""));
        Assert.Equal(WebhookStatus.Accepted, await rig.SendWebhookAsync(FakeRazorpay.CapturedBody("pay_x", "order_nobody_knows", 100)));

        Assert.Equal(OrderStatus.AwaitingPayment, rig.Row(order.Id).Status);
    }

    [Fact]
    public async Task When_the_store_fails_the_provider_is_told_to_send_it_again_and_nothing_is_recorded()
    {
        ThrowingPaymentStore? failing = null;
        var rig = PaymentRig.Create(wrapStore: inner => failing = new ThrowingPaymentStore(inner));
        var order = await rig.PlaceOnlineAsync();
        var paid = await rig.PayAsync(order.Id);
        failing!.Fail = true;

        var status = await rig.SendWebhookAsync(FakeRazorpay.CapturedBody(paid.PaymentId, paid.Start.ProviderOrderId, paid.Start.AmountPaise));

        Assert.Equal(WebhookStatus.Retry, status);
        Assert.Empty(rig.Data.PaymentEvents);
    }

    private sealed class ThrowingPaymentStore(IPaymentStore inner) : IPaymentStore
    {
        public bool Fail { get; set; }

        private void Check()
        {
            if (Fail)
            {
                throw new InvalidOperationException("database is down");
            }
        }

        public Task<PaymentOrderView?> GetOrderAsync(Guid orderId, Guid userId, Guid organizationId, CancellationToken cancellationToken = default) => inner.GetOrderAsync(orderId, userId, organizationId, cancellationToken);
        public Task<Payment?> GetPaymentAsync(Guid orderId, CancellationToken cancellationToken = default) => inner.GetPaymentAsync(orderId, cancellationToken);
        public Task<Payment?> BeginAttemptAsync(Guid orderId, DateTime now, CancellationToken cancellationToken = default) => inner.BeginAttemptAsync(orderId, now, cancellationToken);
        public Task<bool> AttachProviderOrderAsync(Guid paymentId, string providerOrderId, CancellationToken cancellationToken = default) => inner.AttachProviderOrderAsync(paymentId, providerOrderId, cancellationToken);
        public Task<CapturedOutcome> ApplyCapturedAsync(string providerOrderId, string providerPaymentId, long paidPaise, DateTime now, CancellationToken cancellationToken = default) { Check(); return inner.ApplyCapturedAsync(providerOrderId, providerPaymentId, paidPaise, now, cancellationToken); }
        public Task<bool> ApplyFailedAsync(string providerOrderId, string? providerPaymentId, string reason, DateTime now, CancellationToken cancellationToken = default) => inner.ApplyFailedAsync(providerOrderId, providerPaymentId, reason, now, cancellationToken);
        public Task<IReadOnlyList<Guid>> ReleaseExpiredAsync(DateTime now, int max, CancellationToken cancellationToken = default) => inner.ReleaseExpiredAsync(now, max, cancellationToken);
        public Task<IReadOnlyList<Payment>> GetRefundsToStartAsync(int max, CancellationToken cancellationToken = default) => inner.GetRefundsToStartAsync(max, cancellationToken);
        public Task SetRefundStartedAsync(Guid paymentId, string refundId, bool processed, DateTime now, CancellationToken cancellationToken = default) => inner.SetRefundStartedAsync(paymentId, refundId, processed, now, cancellationToken);
        public Task SetRefundFailedAsync(Guid paymentId, string reason, bool permanent, DateTime now, CancellationToken cancellationToken = default) => inner.SetRefundFailedAsync(paymentId, reason, permanent, now, cancellationToken);
        public Task<bool> ApplyRefundResultAsync(string providerPaymentId, string refundId, bool processed, DateTime now, CancellationToken cancellationToken = default) => inner.ApplyRefundResultAsync(providerPaymentId, refundId, processed, now, cancellationToken);
        public Task<IReadOnlyList<Payment>> GetUnsettledAsync(DateTime olderThan, int max, CancellationToken cancellationToken = default) => inner.GetUnsettledAsync(olderThan, max, cancellationToken);
        public Task<bool> TryRecordEventAsync(string provider, string eventId, string type, DateTime now, CancellationToken cancellationToken = default) => inner.TryRecordEventAsync(provider, eventId, type, now, cancellationToken);
    }

    // ---------------- holding the items, and letting go ----------------

    [Fact]
    public async Task Items_are_held_for_15_minutes_then_released_once_the_order_is_cancelled_and_the_customer_told()
    {
        var rig = PaymentRig.Create();
        var before = rig.Stock;
        var order = await rig.PlaceOnlineAsync(quantity: 3);
        rig.Clock.Advance(TimeSpan.FromMinutes(14));

        Assert.Equal(0, await rig.Maintenance.ReleaseExpiredHoldsAsync());
        Assert.Equal(before - 3, rig.Stock);

        rig.Clock.Advance(TimeSpan.FromMinutes(2));
        Assert.Equal(1, await rig.Maintenance.ReleaseExpiredHoldsAsync());

        Assert.Equal(before, rig.Stock);
        var row = rig.Row(order.Id);
        Assert.Equal((OrderStatus.Cancelled, PaymentState.Failed), (row.Status, row.PaymentStatus));
        Assert.Equal([OrderStatus.AwaitingPayment, OrderStatus.Cancelled], row.StatusHistory.Select(history => history.Status));
        Assert.Equal((PaymentState.Failed, "Expired"), (rig.PaymentOf(order.Id).Status, rig.PaymentOf(order.Id).FailureReason));
        Assert.Equal("Payment not completed", Assert.Single(rig.Data.Notifications).Title);
    }

    [Fact]
    public async Task Running_the_release_again_or_twice_in_a_row_returns_the_stock_once()
    {
        var rig = PaymentRig.Create();
        var before = rig.Stock;
        await rig.PlaceOnlineAsync(quantity: 3);
        rig.Clock.Advance(TimeSpan.FromMinutes(20));

        await Task.WhenAll(Enumerable.Range(0, 4).Select(_ => rig.Maintenance.ReleaseExpiredHoldsAsync()));
        await rig.Maintenance.ReleaseExpiredHoldsAsync();

        Assert.Equal(before, rig.Stock);
        Assert.Single(rig.Data.Notifications);
    }

    [Fact]
    public async Task Paid_orders_cash_on_delivery_orders_and_orders_inside_the_hold_are_never_released()
    {
        var rig = PaymentRig.Create();
        var cod = await rig.PlaceOnlineAsync(method: PaymentMethods.CashOnDelivery, key: "cod-key-00000001");
        var paid = await rig.PlaceOnlineAsync(key: "paid-key-0000001");
        var pay = await rig.PayAsync(paid.Id);
        await rig.Payments.ConfirmAsync(paid.Id, pay.Confirm);
        var waiting = await rig.PlaceOnlineAsync(key: "wait-key-0000001");
        rig.Clock.Advance(TimeSpan.FromMinutes(14));

        await rig.Maintenance.ReleaseExpiredHoldsAsync();

        Assert.Equal(OrderStatus.Pending, rig.Row(cod.Id).Status);
        Assert.Equal(OrderStatus.Pending, rig.Row(paid.Id).Status);
        Assert.Equal(OrderStatus.AwaitingPayment, rig.Row(waiting.Id).Status);
    }

    [Fact]
    public async Task The_hold_length_comes_from_the_settings()
    {
        var rig = PaymentRig.Create(tune: settings => settings.HoldMinutes = 30);

        var order = await rig.PlaceOnlineAsync();

        Assert.Equal(rig.Clock.Now.UtcDateTime.AddMinutes(30), order.PaymentExpiresAt);
    }

    // ---------------- refunds ----------------

    [Fact]
    public async Task A_paid_order_the_customer_cancels_is_refunded_in_full_through_the_provider_and_the_customer_is_told()
    {
        var rig = PaymentRig.Create();
        var before = rig.Stock;
        var order = await rig.PlaceOnlineAsync(quantity: 2);
        var paid = await rig.PayAsync(order.Id);
        await rig.Payments.ConfirmAsync(order.Id, paid.Confirm);

        var cancelled = await rig.Orders.CancelAsync(order.Id);
        Assert.Equal(CancelOrderStatus.Succeeded, cancelled.Status);
        Assert.Equal((PaymentState.Refunding, before), (rig.PaymentOf(order.Id).Status, rig.Stock));
        Assert.Equal(PaymentState.Refunding, cancelled.Order!.PaymentStatus);

        Assert.Equal(1, await rig.Maintenance.StartRefundsAsync());

        var refund = rig.Razorpay.Requests.Single(request => request.Path.EndsWith("/refund", StringComparison.Ordinal));
        Assert.Equal($"v1/payments/{paid.PaymentId}/refund", refund.Path);
        using var json = JsonDocument.Parse(refund.Body);
        Assert.Equal(rig.PaymentOf(order.Id).AmountPaise, json.RootElement.GetProperty("amount").GetInt64());
        Assert.Equal((PaymentState.Refunded, PaymentState.Refunded), (rig.PaymentOf(order.Id).Status, rig.Row(order.Id).PaymentStatus));
        Assert.Contains(rig.Data.Notifications, note => note.Title == "Refund processed");
    }

    [Fact]
    public async Task A_refund_still_being_processed_waits_for_the_provider_to_say_it_is_done()
    {
        var rig = PaymentRig.Create();
        rig.Razorpay.RefundStatus = "pending";
        var order = await rig.PlaceOnlineAsync();
        var paid = await rig.PayAsync(order.Id);
        await rig.Payments.ConfirmAsync(order.Id, paid.Confirm);
        await rig.Orders.CancelAsync(order.Id);

        await rig.Maintenance.StartRefundsAsync();

        var payment = rig.PaymentOf(order.Id);
        Assert.Equal(PaymentState.Refunding, payment.Status);
        Assert.NotNull(payment.RefundId);
        Assert.Equal(0, await rig.Maintenance.StartRefundsAsync()); // already started: not asked again

        await rig.SendWebhookAsync(FakeRazorpay.RefundBody(payment.RefundId!, paid.PaymentId));

        Assert.Equal(PaymentState.Refunded, rig.PaymentOf(order.Id).Status);
        Assert.Contains(rig.Data.Notifications, note => note.Title == "Refund processed");
    }

    [Fact]
    public async Task A_paid_order_the_shop_declines_is_refunded_too()
    {
        var rig = PaymentRig.Create();
        var order = await rig.PlaceOnlineAsync();
        var paid = await rig.PayAsync(order.Id);
        await rig.Payments.ConfirmAsync(order.Id, paid.Confirm);

        var rejected = await rig.Data.TryTransitionOrderAsync(order.Id, rig.Data.Organizations.Single().Id, null, OrderStatus.Rejected);

        Assert.Equal(OrderLifecycleStatus.Succeeded, rejected.Status);
        Assert.Equal((PaymentState.Refunding, PaymentState.Refunding), (rig.PaymentOf(order.Id).Status, rig.Row(order.Id).PaymentStatus));
        await rig.Maintenance.StartRefundsAsync();
        Assert.Equal(PaymentState.Refunded, rig.PaymentOf(order.Id).Status);
    }

    [Fact]
    public async Task Cancelling_an_order_that_was_never_paid_has_nothing_to_refund_and_releases_the_items()
    {
        var rig = PaymentRig.Create();
        var before = rig.Stock;
        var order = await rig.PlaceOnlineAsync(quantity: 2);
        await rig.Payments.StartAsync(order.Id);

        var cancelled = await rig.Orders.CancelAsync(order.Id);

        Assert.Equal(CancelOrderStatus.Succeeded, cancelled.Status);
        Assert.Equal((PaymentState.Failed, PaymentState.Failed, before), (rig.PaymentOf(order.Id).Status, rig.Row(order.Id).PaymentStatus, rig.Stock));
        Assert.Equal(0, await rig.Maintenance.StartRefundsAsync());
        Assert.Equal(0, rig.Razorpay.Count(HttpMethod.Post, "v1/payments/"));
    }

    [Fact]
    public async Task When_the_shop_rejects_an_order_its_stock_goes_back_exactly_once()
    {
        var rig = PaymentRig.Create();
        var before = rig.Stock;
        var order = await rig.PlaceOnlineAsync(method: PaymentMethods.CashOnDelivery, quantity: 3);
        Assert.Equal(before - 3, rig.Stock);
        var organization = rig.Data.Organizations.Single().Id;

        var first = await rig.Data.TryTransitionOrderAsync(order.Id, organization, null, OrderStatus.Rejected);
        var second = await rig.Data.TryTransitionOrderAsync(order.Id, organization, null, OrderStatus.Rejected);

        Assert.Equal(OrderLifecycleStatus.Succeeded, first.Status);
        Assert.Equal(OrderLifecycleStatus.InvalidTransition, second.Status);
        Assert.Equal(before, rig.Stock);
    }

    [Fact]
    public async Task Accepting_or_completing_an_order_never_changes_its_stock()
    {
        var rig = PaymentRig.Create();
        var order = await rig.PlaceOnlineAsync(method: PaymentMethods.CashOnDelivery, quantity: 2);
        var held = rig.Stock;
        var organization = rig.Data.Organizations.Single().Id;

        foreach (var step in new[] { OrderStatus.Accepted, OrderStatus.Preparing, OrderStatus.Ready, OrderStatus.Completed })
        {
            Assert.Equal(OrderLifecycleStatus.Succeeded, (await rig.Data.TryTransitionOrderAsync(order.Id, organization, null, step)).Status);
            Assert.Equal(held, rig.Stock);
        }
    }

    [Fact]
    public async Task A_cash_order_cancelled_involves_no_payment_at_all()
    {
        var rig = PaymentRig.Create();
        var order = await rig.PlaceOnlineAsync(method: PaymentMethods.CashOnDelivery);

        await rig.Orders.CancelAsync(order.Id);

        Assert.Empty(rig.Data.Payments);
        Assert.Equal(PaymentState.NotRequired, rig.Row(order.Id).PaymentStatus);
        Assert.Equal(0, await rig.Maintenance.StartRefundsAsync());
    }

    [Fact]
    public async Task A_paid_order_the_shop_has_accepted_cannot_be_cancelled_so_it_is_not_refunded()
    {
        var rig = PaymentRig.Create();
        var order = await rig.PlaceOnlineAsync();
        var paid = await rig.PayAsync(order.Id);
        await rig.Payments.ConfirmAsync(order.Id, paid.Confirm);
        await rig.Data.TryTransitionOrderAsync(order.Id, rig.Data.Organizations.Single().Id, null, OrderStatus.Accepted);

        var result = await rig.Orders.CancelAsync(order.Id);

        Assert.Equal(CancelOrderStatus.NotCancellable, result.Status);
        Assert.Equal(PaymentState.Paid, rig.PaymentOf(order.Id).Status);
    }

    [Fact]
    public async Task When_the_provider_is_busy_the_refund_stays_queued_and_is_tried_again_without_being_lost()
    {
        var rig = PaymentRig.Create();
        var order = await rig.PlaceOnlineAsync();
        var paid = await rig.PayAsync(order.Id);
        await rig.Payments.ConfirmAsync(order.Id, paid.Confirm);
        await rig.Orders.CancelAsync(order.Id);
        rig.Razorpay.FailNext = (HttpStatusCode.ServiceUnavailable, "{}");

        Assert.Equal(0, await rig.Maintenance.StartRefundsAsync());
        Assert.Equal(PaymentState.Refunding, rig.PaymentOf(order.Id).Status);
        Assert.Equal(1, await rig.Maintenance.StartRefundsAsync());

        Assert.Equal(PaymentState.Refunded, rig.PaymentOf(order.Id).Status);
    }

    [Fact]
    public async Task When_the_provider_refuses_for_good_the_refund_is_marked_failed_and_is_not_retried_for_ever()
    {
        var rig = PaymentRig.Create();
        var order = await rig.PlaceOnlineAsync();
        var paid = await rig.PayAsync(order.Id);
        await rig.Payments.ConfirmAsync(order.Id, paid.Confirm);
        await rig.Orders.CancelAsync(order.Id);
        rig.Razorpay.FailNext = (HttpStatusCode.BadRequest, """{"error":{"code":"BAD_REQUEST_ERROR","description":"The refund amount is invalid"}}""");

        await rig.Maintenance.StartRefundsAsync();

        Assert.Equal((PaymentState.RefundFailed, PaymentState.RefundFailed), (rig.PaymentOf(order.Id).Status, rig.Row(order.Id).PaymentStatus));
        Assert.Equal(0, await rig.Maintenance.StartRefundsAsync());
    }

    [Fact]
    public async Task A_refund_that_had_already_gone_through_is_recognised_and_closed()
    {
        var rig = PaymentRig.Create();
        var order = await rig.PlaceOnlineAsync();
        var paid = await rig.PayAsync(order.Id);
        await rig.Payments.ConfirmAsync(order.Id, paid.Confirm);
        await rig.Orders.CancelAsync(order.Id);
        rig.Razorpay.Payments[paid.PaymentId] = rig.Razorpay.Payments[paid.PaymentId] with { Refunded = rig.Razorpay.Payments[paid.PaymentId].Amount };

        await rig.Maintenance.StartRefundsAsync();

        Assert.Equal(PaymentState.Refunded, rig.PaymentOf(order.Id).Status);
    }

    [Fact]
    public async Task A_refund_notice_for_a_payment_we_do_not_know_is_ignored()
    {
        var rig = PaymentRig.Create();

        Assert.Equal(WebhookStatus.Accepted, await rig.SendWebhookAsync(FakeRazorpay.RefundBody("rfnd_x", "pay_unknown")));
    }

    // ---------------- looking up what never settled ----------------

    [Fact]
    public async Task A_payment_the_provider_captured_but_nobody_told_us_about_is_settled_by_the_lookup()
    {
        var rig = PaymentRig.Create();
        var order = await rig.PlaceOnlineAsync();
        var paid = await rig.PayAsync(order.Id); // paid at the provider; the app was closed; the notification was lost

        Assert.Equal(0, await rig.Maintenance.ReconcileAsync());
        rig.Clock.Advance(TimeSpan.FromMinutes(5));
        Assert.Equal(0, await rig.Maintenance.ReconcileAsync()); // too soon to look
        rig.Clock.Advance(TimeSpan.FromMinutes(5));
        rig.Data.Payments.Single().UpdatedAt = rig.Clock.Now.UtcDateTime.AddMinutes(-30);
        Assert.Equal(1, await rig.Maintenance.ReconcileAsync());

        Assert.Equal((OrderStatus.Pending, PaymentState.Paid, paid.PaymentId), (rig.Row(order.Id).Status, rig.PaymentOf(order.Id).Status, rig.PaymentOf(order.Id).ProviderPaymentId));
    }

    [Fact]
    public async Task A_lost_late_payment_is_found_and_refunded_even_after_the_order_was_cancelled()
    {
        var rig = PaymentRig.Create();
        var order = await rig.PlaceOnlineAsync();
        var paid = await rig.PayAsync(order.Id);
        rig.Clock.Advance(TimeSpan.FromMinutes(16));
        await rig.Maintenance.ReleaseExpiredHoldsAsync();
        rig.Data.Payments.Single().UpdatedAt = rig.Clock.Now.UtcDateTime.AddMinutes(-30);

        await rig.Maintenance.ReconcileAsync();
        await rig.Maintenance.StartRefundsAsync();

        Assert.Equal((OrderStatus.Cancelled, PaymentState.Refunded), (rig.Row(order.Id).Status, rig.PaymentOf(order.Id).Status));
        Assert.Equal(paid.PaymentId, rig.PaymentOf(order.Id).ProviderPaymentId);
    }

    [Fact]
    public async Task A_refund_the_provider_finished_without_telling_us_is_closed_by_the_lookup()
    {
        var rig = PaymentRig.Create();
        rig.Razorpay.RefundStatus = "pending";
        var order = await rig.PlaceOnlineAsync();
        var paid = await rig.PayAsync(order.Id);
        await rig.Payments.ConfirmAsync(order.Id, paid.Confirm);
        await rig.Orders.CancelAsync(order.Id);
        await rig.Maintenance.StartRefundsAsync();
        rig.Razorpay.Payments[paid.PaymentId] = rig.Razorpay.Payments[paid.PaymentId] with { Refunded = rig.PaymentOf(order.Id).AmountPaise };
        rig.Clock.Advance(TimeSpan.FromMinutes(30));

        var changed = await rig.Maintenance.ReconcileAsync();

        Assert.Equal(1, changed);
        Assert.Equal(PaymentState.Refunded, rig.PaymentOf(order.Id).Status);
    }

    [Fact]
    public async Task A_lookup_that_cannot_reach_the_provider_leaves_everything_as_it_was()
    {
        var rig = PaymentRig.Create();
        var order = await rig.PlaceOnlineAsync();
        await rig.PayAsync(order.Id);
        rig.Clock.Advance(TimeSpan.FromMinutes(30));
        rig.Razorpay.Down = true;

        Assert.Equal(0, await rig.Maintenance.ReconcileAsync());

        Assert.Equal(OrderStatus.AwaitingPayment, rig.Row(order.Id).Status);
    }

    // ---------------- settings, routes, and the secrets ----------------

    [Fact]
    public void Settings_that_cannot_work_stop_the_start_up_but_a_switched_off_section_needs_nothing()
    {
        new PaymentSettings().Validate();
        Assert.Throws<InvalidOperationException>(new PaymentSettings { Enabled = true, KeyId = "nope", KeySecret = new string('s', 20), WebhookSecret = new string('w', 20) }.Validate);
        Assert.Throws<InvalidOperationException>(new PaymentSettings { Enabled = true, KeyId = "rzp_test_x", KeySecret = "short", WebhookSecret = new string('w', 20) }.Validate);
        Assert.Throws<InvalidOperationException>(new PaymentSettings { Enabled = true, KeyId = "rzp_test_x", KeySecret = new string('s', 20), WebhookSecret = "short" }.Validate);
        new PaymentSettings { Enabled = true, KeyId = "rzp_test_x", KeySecret = new string('s', 20), WebhookSecret = "" }.Validate(); // the webhook is optional
        Assert.Throws<InvalidOperationException>(new PaymentSettings { HoldMinutes = 2 }.Validate);
        Assert.Throws<InvalidOperationException>(new PaymentSettings { HoldMinutes = 90 }.Validate);
        Assert.Throws<InvalidOperationException>(new PaymentSettings { JobIntervalSeconds = 1 }.Validate);
        new PaymentSettings { Enabled = true, KeyId = "rzp_test_x", KeySecret = new string('s', 20), WebhookSecret = new string('w', 20) }.Validate();
    }

    [Fact]
    public async Task The_routes_answer_with_the_right_codes()
    {
        var rig = PaymentRig.Create();
        var controller = new PaymentsController(rig.Payments, rig.Webhooks, rig.Settings) { ControllerContext = new ControllerContext { HttpContext = new Microsoft.AspNetCore.Http.DefaultHttpContext() } };
        var order = await rig.PlaceOnlineAsync();

        var options = Assert.IsType<OkObjectResult>(controller.Options());
        var started = Assert.IsType<OkObjectResult>(await controller.Start(order.Id, CancellationToken.None));
        var missing = Assert.IsType<NotFoundObjectResult>(await controller.Start(Guid.NewGuid(), CancellationToken.None));
        var bad = Assert.IsType<ConflictObjectResult>(await controller.Confirm(order.Id, new ConfirmPaymentRequest("order_x", "pay_x", "sig"), CancellationToken.None));

        Assert.Contains("\"online\":true", JsonSerializer.Serialize(options.Value));
        Assert.Contains(FakeRazorpay.KeyId, JsonSerializer.Serialize(started.Value));
        Assert.Equal(false, bad.Value!.GetType().GetProperty("success")!.GetValue(bad.Value));
        Assert.Equal(PaymentReasons.Mismatch, bad.Value.GetType().GetProperty("reason")!.GetValue(bad.Value));
        Assert.NotNull(missing.Value);
    }

    [Fact]
    public async Task The_webhook_route_reads_the_raw_body_and_answers_200_400_or_500()
    {
        var rig = PaymentRig.Create();
        var order = await rig.PlaceOnlineAsync();
        var paid = await rig.PayAsync(order.Id);
        var body = FakeRazorpay.CapturedBody(paid.PaymentId, paid.Start.ProviderOrderId, paid.Start.AmountPaise);

        IActionResult Send(string text, string? signature)
        {
            var http = new Microsoft.AspNetCore.Http.DefaultHttpContext();
            http.Request.Body = new MemoryStream(System.Text.Encoding.UTF8.GetBytes(text));
            http.Request.ContentLength = text.Length;
            if (signature is not null)
            {
                http.Request.Headers["X-Razorpay-Signature"] = signature;
            }

            http.Request.Headers["X-Razorpay-Event-Id"] = "evt_route";
            var controller = new PaymentsController(rig.Payments, rig.Webhooks, rig.Settings) { ControllerContext = new ControllerContext { HttpContext = http } };
            return controller.Webhook(CancellationToken.None).GetAwaiter().GetResult();
        }

        Assert.IsType<BadRequestObjectResult>(Send(body, "wrong"));
        Assert.IsType<BadRequestObjectResult>(Send(body, null));
        Assert.Equal(OrderStatus.AwaitingPayment, rig.Row(order.Id).Status);
        Assert.IsType<OkObjectResult>(Send(body, FakeRazorpay.SignWebhook(body)));
        Assert.Equal(OrderStatus.Pending, rig.Row(order.Id).Status);
    }

    [Fact]
    public async Task The_checkout_route_passes_the_online_choice_through_and_the_order_answer_carries_the_payment_state()
    {
        var rig = PaymentRig.Create();
        await rig.Cart.AddItemAsync(rig.Store.Id, new AddCartItemRequest(rig.Tomato.Id, 1));
        var controller = new CheckoutController(rig.Checkout) { ControllerContext = new ControllerContext { HttpContext = new Microsoft.AspNetCore.Http.DefaultHttpContext() } };
        controller.HttpContext.Request.Headers["Idempotency-Key"] = "route-key-0000001";

        var created = Assert.IsType<CreatedResult>(await controller.Checkout(rig.Store.Id, rig.Where(), CancellationToken.None));

        var order = (OrderResponse)created.Value!.GetType().GetProperty("data")!.GetValue(created.Value)!;
        Assert.Equal((OrderStatus.AwaitingPayment, PaymentState.Created), (order.Status, order.PaymentStatus));
    }
}
