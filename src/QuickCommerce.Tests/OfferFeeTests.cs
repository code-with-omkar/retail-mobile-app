using System.Net;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using QuickCommerce.Api.Controllers;
using QuickCommerce.Application.DTOs;
using QuickCommerce.Application.Interfaces;
using QuickCommerce.Application.Services;
using QuickCommerce.Domain;
using QuickCommerce.Infrastructure;
using QuickCommerce.Infrastructure.Payments;
using QuickCommerce.Infrastructure.Persistence;
using Xunit;

namespace QuickCommerce.Tests;

/// <summary>Phase P14: a store pays a configurable fee, online, before its offer goes live; cancelling before it was sent refunds a paid fee.</summary>
public sealed class OfferFeeTests
{
    private sealed class FixedScope(bool admin, Guid organizationId) : IAuthorizationScopeService
    {
        private AuthorizationScope Scope => new(Guid.NewGuid(), organizationId, new HashSet<string> { admin ? "ApplicationAdmin" : "StoreStaff" }, new HashSet<Guid>(), new HashSet<string>());
        public Task<AuthorizationScope?> ResolveAsync(CancellationToken cancellationToken = default) => Task.FromResult<AuthorizationScope?>(Scope);
        public Task<AuthorizationScope?> ResolveForUserAsync(Guid userId, CancellationToken cancellationToken = default) => Task.FromResult<AuthorizationScope?>(Scope);
        public Task<bool> HasPermissionAsync(string permissionCode, CancellationToken cancellationToken = default) => Task.FromResult(admin);
        public Task<bool> CanAccessStoreAsync(Guid storeId, CancellationToken cancellationToken = default) => Task.FromResult(admin);
        public Task<bool> OwnsCustomerAsync(Guid customerId, CancellationToken cancellationToken = default) => Task.FromResult(false);
    }

    private sealed class FixedUser(UserContext? context) : ICurrentUserContextResolver
    {
        public Task<UserContext?> ResolveAsync(CancellationToken cancellationToken = default) => Task.FromResult(context);
    }

    /// <summary>One actor's view of the services: an administrator, or store staff of one store.</summary>
    private sealed class Actor
    {
        public required CampaignAdminService Campaigns { get; init; }
        public required CampaignPaymentService Payments { get; init; }
        public required OfferFeeService Fees { get; init; }
    }

    private sealed class Rig
    {
        public required InMemoryCommerceStore Data { get; init; }
        public required TestClock Clock { get; init; }
        public required FakeRazorpay Razorpay { get; init; }
        public required PaymentSettings Settings { get; init; }
        public required Store A { get; init; }
        public required Store B { get; init; }
        public required Actor Admin { get; init; }
        public required Actor StaffA { get; init; }
        public required Actor StaffB { get; init; }
        public required CampaignPaymentMaintenance Upkeep { get; init; }
        public required RazorpayGateway Gateway { get; init; }
    }

    private static Rig Create(bool paymentsOn = true)
    {
        var data = InMemoryCommerceStore.CreateSeeded();
        var stores = data.Stores.Take(2).ToArray();
        var organization = data.Organizations.Single().Id;
        var user = data.Users.Single().Id;
        var clock = new TestClock(new DateTimeOffset(2026, 10, 12, 10, 0, 0, TimeSpan.Zero));
        var settings = new PaymentSettings { Enabled = paymentsOn, KeyId = FakeRazorpay.KeyId, KeySecret = FakeRazorpay.KeySecret, WebhookSecret = FakeRazorpay.WebhookSecret };
        var razorpay = new FakeRazorpay();
        var gateway = new RazorpayGateway(new HttpClient(razorpay) { BaseAddress = new Uri(RazorpayGateway.BaseAddress) }, settings);
        Actor Make(bool admin, Store? store)
        {
            var scope = new FixedScope(admin, organization);
            var resolver = new FixedUser(admin ? new UserContext(user, organization, null, Role.Admin) : new UserContext(user, organization, store!.Id, Role.StoreStaff));
            return new Actor
            {
                Campaigns = new CampaignAdminService(data, scope, resolver, clock, data, settings),
                Payments = new CampaignPaymentService(data, data, gateway, settings, scope, resolver, clock, NullLogger<CampaignPaymentService>.Instance),
                Fees = new OfferFeeService(data, scope, resolver, settings, clock)
            };
        }

        return new Rig
        {
            Data = data,
            Clock = clock,
            Razorpay = razorpay,
            Settings = settings,
            Gateway = gateway,
            A = stores[0],
            B = stores[1],
            Admin = Make(true, null),
            StaffA = Make(false, stores[0]),
            StaffB = Make(false, stores[1]),
            Upkeep = new CampaignPaymentMaintenance(data, gateway, settings, clock, NullLogger<CampaignPaymentMaintenance>.Instance)
        };
    }

    private static Task<CampaignPayment?> PaymentOf(Rig rig, Guid campaignId) => ((ICampaignPaymentStore)rig.Data).GetPaymentAsync(campaignId);

    private const int Fee = 50_000; // ₹500

    private static async Task SetFee(Rig rig, int paise = Fee) => Assert.Equal(CampaignOperationStatus.Succeeded, (await rig.Admin.Fees.SetDefaultAsync(paise)).Status);

    private static async Task<CampaignResponse> StaffOffer(Rig rig, string title = "Fruit day") => (await rig.StaffA.Campaigns.CreateAsync(new CampaignRequest(title, "20% off all fruit."))).Value!;

    private static async Task<(StartPaymentResponse Start, string PaymentId)> PayFee(Rig rig, CampaignResponse offer, string status = "captured", long? amount = null)
    {
        var start = (await rig.StaffA.Payments.StartAsync(offer.Id)).Value!;
        var paymentId = rig.Razorpay.PayOrder(start.ProviderOrderId, amount ?? start.AmountPaise, status);
        return (start, paymentId);
    }

    private static ConfirmPaymentRequest Proof(StartPaymentResponse start, string paymentId) => new(start.ProviderOrderId, paymentId, FakeRazorpay.Sign(start.ProviderOrderId, paymentId));

    // ---------------- the fee settings ----------------

    [Fact]
    public async Task The_default_fee_and_a_stores_own_fee_decide_what_each_store_pays_and_removing_the_own_fee_goes_back_to_the_default()
    {
        var rig = Create();

        Assert.Equal(0, (await rig.Admin.Fees.GetAsync()).Value!.DefaultPaise);
        await SetFee(rig, 50_000);
        await rig.Admin.Fees.SetStoreAsync(rig.B.Id, 20_000);
        var both = (await rig.Admin.Fees.GetAsync()).Value!;

        Assert.Equal(50_000, both.DefaultPaise);
        Assert.Equal((50_000, (int?)null), (both.Stores.Single(store => store.StoreId == rig.A.Id).EffectivePaise, both.Stores.Single(store => store.StoreId == rig.A.Id).OverridePaise));
        Assert.Equal((20_000, (int?)20_000), (both.Stores.Single(store => store.StoreId == rig.B.Id).EffectivePaise, both.Stores.Single(store => store.StoreId == rig.B.Id).OverridePaise));
        Assert.Equal(50_000, (await rig.StaffA.Fees.GetMineAsync()).Value!.FeePaise);
        Assert.Equal(20_000, (await rig.StaffB.Fees.GetMineAsync()).Value!.FeePaise);

        await rig.Admin.Fees.SetStoreAsync(rig.B.Id, null);
        Assert.Equal(50_000, (await rig.StaffB.Fees.GetMineAsync()).Value!.FeePaise);
        await rig.Admin.Fees.SetStoreAsync(rig.A.Id, 0);
        Assert.Equal(0, (await rig.StaffA.Fees.GetMineAsync()).Value!.FeePaise);
    }

    [Fact]
    public async Task Only_an_administrator_changes_fees_and_bad_amounts_and_unknown_stores_are_refused()
    {
        var rig = Create();

        Assert.Equal(CampaignOperationStatus.Unauthorized, (await rig.StaffA.Fees.GetAsync()).Status);
        Assert.Equal(CampaignOperationStatus.Unauthorized, (await rig.StaffA.Fees.SetDefaultAsync(100)).Status);
        Assert.Equal(CampaignOperationStatus.Unauthorized, (await rig.StaffA.Fees.SetStoreAsync(rig.A.Id, 100)).Status);
        Assert.Equal(CampaignOperationStatus.InvalidRequest, (await rig.Admin.Fees.SetDefaultAsync(-1)).Status);
        Assert.Equal(CampaignOperationStatus.InvalidRequest, (await rig.Admin.Fees.SetDefaultAsync(OfferFeeService.MaxPaise + 1)).Status);
        Assert.Equal(CampaignOperationStatus.InvalidRequest, (await rig.Admin.Fees.SetStoreAsync(rig.A.Id, -5)).Status);
        Assert.Equal(CampaignOperationStatus.NotFound, (await rig.Admin.Fees.SetStoreAsync(Guid.NewGuid(), 100)).Status);
        Assert.Equal(0, await rig.Data.GetDefaultAsync());
    }

    [Fact]
    public async Task What_I_would_pay_is_zero_for_an_administrator_the_stores_fee_for_staff_and_nothing_for_anyone_else()
    {
        var rig = Create(paymentsOn: false);
        await SetFee(rig);
        var organization = rig.Data.Organizations.Single().Id;
        var customer = new OfferFeeService(rig.Data, new FixedScope(false, organization), new FixedUser(new UserContext(Guid.NewGuid(), organization, null, Role.Customer)), rig.Settings, rig.Clock);

        var admin = (await rig.Admin.Fees.GetMineAsync()).Value!;
        var staff = (await rig.StaffA.Fees.GetMineAsync()).Value!;

        Assert.Equal((0, false), (admin.FeePaise, admin.PaymentsAvailable));
        Assert.Equal((Fee, false), (staff.FeePaise, staff.PaymentsAvailable));
        Assert.Equal(CampaignOperationStatus.Unauthorized, (await customer.GetMineAsync()).Status);
    }

    // ---------------- publishing needs the fee ----------------

    [Fact]
    public async Task A_store_offer_with_a_fee_waits_for_payment_and_is_not_sent_until_it_is_paid()
    {
        var rig = Create();
        await SetFee(rig);

        var offer = await StaffOffer(rig);
        Assert.Equal((CampaignStatuses.AwaitingPayment, Fee, PaymentState.Created), (offer.Status, offer.FeePaise, offer.PaymentStatus));
        Assert.Equal(0, await rig.Data.PublishDueCampaignsAsync(DateTime.UtcNow.AddMinutes(5)));
        Assert.Empty(rig.Data.Notifications);

        var (start, paymentId) = await PayFee(rig, offer);
        var paid = await rig.StaffA.Payments.ConfirmAsync(offer.Id, Proof(start, paymentId));

        Assert.Equal((PaymentOperationStatus.Succeeded, CampaignStatuses.Scheduled, PaymentState.Paid), (paid.Status, paid.Value!.Status, paid.Value.PaymentStatus));
        Assert.Equal(1, await rig.Data.PublishDueCampaignsAsync(DateTime.UtcNow.AddMinutes(5)));
    }

    [Fact]
    public async Task A_free_store_offer_and_every_administrator_offer_need_no_payment()
    {
        var rig = Create();
        await SetFee(rig);
        await rig.Admin.Fees.SetStoreAsync(rig.B.Id, 0);

        var admin = (await rig.Admin.Campaigns.CreateAsync(new CampaignRequest("From the business", "body"))).Value!;
        var freeStore = (await rig.StaffB.Campaigns.CreateAsync(new CampaignRequest("Free store", "body"))).Value!;

        Assert.All(new[] { admin, freeStore }, offer => Assert.Equal((CampaignStatuses.Scheduled, 0, PaymentState.NotRequired), (offer.Status, offer.FeePaise, offer.PaymentStatus)));
        Assert.Equal(2, await rig.Data.PublishDueCampaignsAsync(DateTime.UtcNow.AddMinutes(1)));
    }

    [Fact]
    public async Task While_online_payment_is_off_an_offer_with_a_fee_cannot_be_created_but_a_free_one_can()
    {
        var rig = Create(paymentsOn: false);
        await SetFee(rig);

        var refused = await rig.StaffA.Campaigns.CreateAsync(new CampaignRequest("t", "b"));
        await rig.Admin.Fees.SetStoreAsync(rig.A.Id, 0);
        var allowed = await rig.StaffA.Campaigns.CreateAsync(new CampaignRequest("t", "b"));

        Assert.Equal((CampaignOperationStatus.Conflict, PaymentReasons.PaymentsUnavailable), (refused.Status, refused.Reason));
        Assert.Equal(CampaignOperationStatus.Succeeded, allowed.Status);
        Assert.Single(rig.Data.Campaigns);
    }

    [Fact]
    public async Task The_fee_is_the_one_in_force_when_the_offer_was_created_and_later_changes_do_not_touch_it()
    {
        var rig = Create();
        await SetFee(rig, 50_000);
        var offer = await StaffOffer(rig);

        await SetFee(rig, 90_000);

        Assert.Equal(50_000, (await rig.StaffA.Campaigns.ListAsync()).Value!.Single().FeePaise);
        Assert.Equal(50_000, (await rig.StaffA.Payments.StartAsync(offer.Id)).Value!.AmountPaise);
    }

    // ---------------- paying ----------------

    [Fact]
    public async Task Starting_gives_the_checkout_what_it_needs_for_exactly_the_fee_and_no_secret()
    {
        var rig = Create();
        await SetFee(rig);
        var offer = await StaffOffer(rig);

        var started = await rig.StaffA.Payments.StartAsync(offer.Id);

        Assert.Equal(PaymentOperationStatus.Succeeded, started.Status);
        Assert.Equal(("Razorpay", FakeRazorpay.KeyId, "INR", (long)Fee), (started.Value!.Provider, started.Value.KeyId, started.Value.Currency, started.Value.AmountPaise));
        Assert.Equal(rig.Razorpay.Orders[started.Value.ProviderOrderId], started.Value.AmountPaise);
        var serialized = System.Text.Json.JsonSerializer.Serialize(started);
        Assert.DoesNotContain(FakeRazorpay.KeySecret, serialized);
        Assert.DoesNotContain(FakeRazorpay.WebhookSecret, serialized);
    }

    [Fact]
    public async Task Starting_again_reuses_the_provider_order_so_the_fee_is_never_charged_twice()
    {
        var rig = Create();
        await SetFee(rig);
        var offer = await StaffOffer(rig);

        var first = (await rig.StaffA.Payments.StartAsync(offer.Id)).Value!;
        var second = (await rig.StaffA.Payments.StartAsync(offer.Id)).Value!;

        Assert.Equal(first.ProviderOrderId, second.ProviderOrderId);
        Assert.Equal(1, rig.Razorpay.Count(HttpMethod.Post, "v1/orders"));
        Assert.Equal(2, (await PaymentOf(rig, offer.Id))!.Attempts);
    }

    [Fact]
    public async Task Only_the_offers_own_store_or_an_administrator_can_pay_and_other_stores_see_nothing()
    {
        var rig = Create();
        await SetFee(rig);
        var offer = await StaffOffer(rig);

        Assert.Equal(PaymentOperationStatus.NotFound, (await rig.StaffB.Payments.StartAsync(offer.Id)).Status);
        Assert.Equal(PaymentOperationStatus.NotFound, (await rig.StaffB.Payments.ConfirmAsync(offer.Id, new ConfirmPaymentRequest("o", "p", "s"))).Status);
        Assert.Equal(PaymentOperationStatus.NotFound, (await rig.StaffA.Payments.StartAsync(Guid.NewGuid())).Status);
        Assert.Equal(PaymentOperationStatus.Succeeded, (await rig.Admin.Payments.StartAsync(offer.Id)).Status);
    }

    [Fact]
    public async Task An_offer_that_needs_no_payment_a_paid_one_or_a_cancelled_one_cannot_be_paid()
    {
        var rig = Create();
        var free = (await rig.StaffA.Campaigns.CreateAsync(new CampaignRequest("free", "b"))).Value!;
        await SetFee(rig);
        var paidOne = await StaffOffer(rig, "paid");
        var (start, paymentId) = await PayFee(rig, paidOne);
        await rig.StaffA.Payments.ConfirmAsync(paidOne.Id, Proof(start, paymentId));
        var cancelled = await StaffOffer(rig, "cancelled");
        await rig.StaffA.Campaigns.CancelAsync(cancelled.Id);

        Assert.Equal(CampaignReasons.NotAwaitingPayment, (await rig.StaffA.Payments.StartAsync(free.Id)).Reason);
        Assert.Equal(PaymentReasons.AlreadyPaid, (await rig.StaffA.Payments.StartAsync(paidOne.Id)).Reason);
        Assert.Equal(CampaignReasons.NotAwaitingPayment, (await rig.StaffA.Payments.StartAsync(cancelled.Id)).Reason);
    }

    [Fact]
    public async Task Starting_is_refused_while_online_payment_is_off_and_when_the_provider_cannot_be_reached_it_works_on_the_next_try()
    {
        var rig = Create();
        await SetFee(rig);
        var offer = await StaffOffer(rig);
        rig.Razorpay.Down = true;

        var down = await rig.StaffA.Payments.StartAsync(offer.Id);
        rig.Razorpay.Down = false;
        var later = await rig.StaffA.Payments.StartAsync(offer.Id);
        rig.Settings.Enabled = false;
        var off = await rig.StaffA.Payments.StartAsync(offer.Id);

        Assert.Equal(PaymentReasons.ProviderUnavailable, down.Reason);
        Assert.Equal(PaymentOperationStatus.Succeeded, later.Status);
        Assert.Equal(PaymentReasons.PaymentsUnavailable, off.Reason);
        Assert.Single(rig.Razorpay.Orders);
    }

    [Fact]
    public async Task Confirming_twice_changes_nothing_the_second_time()
    {
        var rig = Create();
        await SetFee(rig);
        var offer = await StaffOffer(rig);
        var (start, paymentId) = await PayFee(rig, offer);

        var first = await rig.StaffA.Payments.ConfirmAsync(offer.Id, Proof(start, paymentId));
        var second = await rig.StaffA.Payments.ConfirmAsync(offer.Id, Proof(start, paymentId));

        Assert.Equal(PaymentOperationStatus.Succeeded, first.Status);
        Assert.Equal(PaymentOperationStatus.Succeeded, second.Status);
        Assert.Equal(PaymentState.Paid, (await PaymentOf(rig, offer.Id))!.Status);
    }

    [Theory]
    [InlineData("")]
    [InlineData("not-a-signature")]
    [InlineData("0000000000000000000000000000000000000000000000000000000000000000")]
    public async Task A_made_up_signature_never_pays_the_fee(string signature)
    {
        var rig = Create();
        await SetFee(rig);
        var offer = await StaffOffer(rig);
        var (start, paymentId) = await PayFee(rig, offer);

        var result = await rig.StaffA.Payments.ConfirmAsync(offer.Id, new ConfirmPaymentRequest(start.ProviderOrderId, paymentId, signature));

        Assert.NotEqual(PaymentOperationStatus.Succeeded, result.Status);
        Assert.Equal(PaymentState.Created, (await PaymentOf(rig, offer.Id))!.Status);
    }

    [Fact]
    public async Task A_payment_for_another_offer_or_not_yet_captured_is_not_accepted_and_a_smaller_payment_is_refunded()
    {
        var rig = Create();
        await SetFee(rig);
        var offer = await StaffOffer(rig, "one");
        var other = await StaffOffer(rig, "two");
        var (otherStart, otherPayment) = await PayFee(rig, other);
        var (start, pending) = await PayFee(rig, offer, status: "authorized");

        var wrongOffer = await rig.StaffA.Payments.ConfirmAsync(offer.Id, Proof(otherStart, otherPayment));
        var notCaptured = await rig.StaffA.Payments.ConfirmAsync(offer.Id, Proof(start, pending));
        var shortPaid = rig.Razorpay.PayOrder(start.ProviderOrderId, Fee - 100);
        var mismatch = await rig.StaffA.Payments.ConfirmAsync(offer.Id, Proof(start, shortPaid));

        Assert.Equal(PaymentReasons.Mismatch, wrongOffer.Reason);
        Assert.Equal(PaymentReasons.NotCaptured, notCaptured.Reason);
        Assert.Equal(PaymentReasons.AmountMismatch, mismatch.Reason);
        Assert.Equal((PaymentState.Refunding, PaymentState.Created), ((await PaymentOf(rig, offer.Id))!.Status, (await rig.StaffA.Campaigns.ListAsync()).Value!.Single(item => item.Id == offer.Id).PaymentStatus));
        Assert.Equal(1, await rig.Upkeep.StartRefundsAsync());
    }

    [Fact]
    public async Task Bad_requests_are_refused_and_when_the_provider_cannot_be_asked_the_fee_is_settled_by_the_lookup_later()
    {
        var rig = Create();
        await SetFee(rig);
        var offer = await StaffOffer(rig);
        var (start, paymentId) = await PayFee(rig, offer);

        Assert.Equal(PaymentOperationStatus.InvalidRequest, (await rig.StaffA.Payments.ConfirmAsync(offer.Id, new ConfirmPaymentRequest("o", " ", "s"))).Status);
        rig.Razorpay.Down = true;
        var down = await rig.StaffA.Payments.ConfirmAsync(offer.Id, Proof(start, paymentId));
        rig.Razorpay.Down = false;
        rig.Clock.Advance(TimeSpan.FromMinutes(30));
        var changed = await rig.Upkeep.ReconcileAsync();

        Assert.Equal(PaymentReasons.ProviderUnavailable, down.Reason);
        Assert.Equal(1, changed);
        Assert.Equal((PaymentState.Paid, paymentId), ((await PaymentOf(rig, offer.Id))!.Status, (await PaymentOf(rig, offer.Id))!.ProviderPaymentId));
        Assert.Equal(1, await rig.Data.PublishDueCampaignsAsync(DateTime.UtcNow.AddMinutes(5)));
    }

    // ---------------- cancelling and refunds ----------------

    [Fact]
    public async Task Cancelling_an_unpaid_offer_closes_its_payment_with_nothing_to_refund()
    {
        var rig = Create();
        await SetFee(rig);
        var offer = await StaffOffer(rig);
        await rig.StaffA.Payments.StartAsync(offer.Id);

        var cancelled = await rig.StaffA.Campaigns.CancelAsync(offer.Id);

        Assert.Equal((CampaignStatuses.Cancelled, PaymentState.Failed), (cancelled.Value!.Status, cancelled.Value.PaymentStatus));
        Assert.Equal(PaymentState.Failed, (await PaymentOf(rig, offer.Id))!.Status);
        Assert.Equal(0, await rig.Upkeep.StartRefundsAsync());
        Assert.Equal(0, rig.Razorpay.Count(HttpMethod.Post, "v1/payments/"));
    }

    [Fact]
    public async Task Cancelling_a_paid_offer_before_it_is_sent_refunds_the_whole_fee()
    {
        var rig = Create();
        await SetFee(rig);
        var offer = (await rig.StaffA.Campaigns.CreateAsync(new CampaignRequest("later", "body", StartsAt: rig.Clock.Now.UtcDateTime.AddDays(2)))).Value!;
        var (start, paymentId) = await PayFee(rig, offer);
        await rig.StaffA.Payments.ConfirmAsync(offer.Id, Proof(start, paymentId));

        var cancelled = await rig.StaffA.Campaigns.CancelAsync(offer.Id);
        Assert.Equal(PaymentState.Refunding, cancelled.Value!.PaymentStatus);
        Assert.Equal(1, await rig.Upkeep.StartRefundsAsync());

        var after = (await rig.StaffA.Campaigns.ListAsync()).Value!.Single();
        Assert.Equal((CampaignStatuses.Cancelled, PaymentState.Refunded), (after.Status, after.PaymentStatus));
        Assert.Equal($"v1/payments/{paymentId}/refund", rig.Razorpay.Requests.Single(request => request.Path.EndsWith("/refund", StringComparison.Ordinal)).Path);
        Assert.Equal(0, await rig.Data.PublishDueCampaignsAsync(DateTime.UtcNow.AddDays(3)));
        Assert.Equal(PaymentOperationStatus.Succeeded, (await rig.StaffA.Campaigns.CancelAsync(offer.Id)).Status == CampaignOperationStatus.Succeeded ? PaymentOperationStatus.Succeeded : PaymentOperationStatus.Conflict);
    }

    [Fact]
    public async Task A_sent_offer_cannot_be_cancelled_so_its_fee_is_not_refunded()
    {
        var rig = Create();
        await SetFee(rig);
        var offer = await StaffOffer(rig);
        var (start, paymentId) = await PayFee(rig, offer);
        await rig.StaffA.Payments.ConfirmAsync(offer.Id, Proof(start, paymentId));
        await rig.Data.PublishDueCampaignsAsync(DateTime.UtcNow.AddMinutes(1));

        var result = await rig.StaffA.Campaigns.CancelAsync(offer.Id);

        Assert.Equal((CampaignOperationStatus.Conflict, CampaignReasons.AlreadySent), (result.Status, result.Reason));
        Assert.Equal(PaymentState.Paid, (await PaymentOf(rig, offer.Id))!.Status);
    }

    [Fact]
    public async Task A_payment_that_lands_after_the_offer_was_cancelled_is_refunded()
    {
        var rig = Create();
        await SetFee(rig);
        var offer = await StaffOffer(rig);
        var (start, paymentId) = await PayFee(rig, offer);
        await rig.StaffA.Campaigns.CancelAsync(offer.Id);

        var late = await rig.StaffA.Payments.ConfirmAsync(offer.Id, Proof(start, paymentId));

        Assert.Equal(CampaignReasons.NotAwaitingPayment, late.Reason);
        Assert.Equal(PaymentState.Refunding, (await PaymentOf(rig, offer.Id))!.Status);
        Assert.Equal(1, await rig.Upkeep.StartRefundsAsync());
        Assert.Equal(PaymentState.Refunded, (await PaymentOf(rig, offer.Id))!.Status);
    }

    [Fact]
    public async Task A_refund_the_provider_is_still_processing_is_closed_by_the_lookup_and_a_busy_provider_keeps_it_queued()
    {
        var rig = Create();
        await SetFee(rig);
        var offer = (await rig.StaffA.Campaigns.CreateAsync(new CampaignRequest("later", "body", StartsAt: rig.Clock.Now.UtcDateTime.AddDays(2)))).Value!;
        var (start, paymentId) = await PayFee(rig, offer);
        await rig.StaffA.Payments.ConfirmAsync(offer.Id, Proof(start, paymentId));
        await rig.StaffA.Campaigns.CancelAsync(offer.Id);
        rig.Razorpay.FailNext = (HttpStatusCode.ServiceUnavailable, "{}");
        Assert.Equal(0, await rig.Upkeep.StartRefundsAsync());
        Assert.Equal(PaymentState.Refunding, (await PaymentOf(rig, offer.Id))!.Status);

        rig.Razorpay.RefundStatus = "pending";
        Assert.Equal(1, await rig.Upkeep.StartRefundsAsync());
        Assert.Equal(PaymentState.Refunding, (await PaymentOf(rig, offer.Id))!.Status);
        rig.Razorpay.Payments[paymentId] = rig.Razorpay.Payments[paymentId] with { Refunded = rig.Razorpay.Payments[paymentId].Amount };
        rig.Clock.Advance(TimeSpan.FromMinutes(30));

        Assert.Equal(1, await rig.Upkeep.ReconcileAsync());
        Assert.Equal(PaymentState.Refunded, (await PaymentOf(rig, offer.Id))!.Status);
    }

    [Fact]
    public async Task A_refund_the_provider_refuses_for_good_is_marked_failed_and_not_retried()
    {
        var rig = Create();
        await SetFee(rig);
        var offer = (await rig.StaffA.Campaigns.CreateAsync(new CampaignRequest("later", "body", StartsAt: rig.Clock.Now.UtcDateTime.AddDays(2)))).Value!;
        var (start, paymentId) = await PayFee(rig, offer);
        await rig.StaffA.Payments.ConfirmAsync(offer.Id, Proof(start, paymentId));
        await rig.StaffA.Campaigns.CancelAsync(offer.Id);
        rig.Razorpay.FailNext = (HttpStatusCode.BadRequest, """{"error":{"code":"BAD_REQUEST_ERROR","description":"The refund amount is invalid"}}""");

        await rig.Upkeep.StartRefundsAsync();

        Assert.Equal(PaymentState.RefundFailed, (await PaymentOf(rig, offer.Id))!.Status);
        Assert.Equal(PaymentState.RefundFailed, (await rig.StaffA.Campaigns.ListAsync()).Value!.Single().PaymentStatus);
        Assert.Equal(0, await rig.Upkeep.StartRefundsAsync());
    }

    // ---------------- routes ----------------

    [Fact]
    public async Task The_routes_answer_with_the_right_codes()
    {
        var rig = Create();
        await SetFee(rig);
        var offer = await StaffOffer(rig);
        var http = new ControllerContext { HttpContext = new DefaultHttpContext() };
        var campaigns = new CampaignsController(rig.StaffA.Campaigns, rig.StaffA.Payments) { ControllerContext = http };
        var fees = new OfferFeesController(rig.Admin.Fees) { ControllerContext = http };

        Assert.IsType<OkObjectResult>(await campaigns.StartPayment(offer.Id, CancellationToken.None));
        Assert.IsType<NotFoundObjectResult>(await campaigns.StartPayment(Guid.NewGuid(), CancellationToken.None));
        Assert.IsType<ConflictObjectResult>(await campaigns.ConfirmPayment(offer.Id, new ConfirmPaymentRequest("order_x", "pay_x", "sig"), CancellationToken.None));
        Assert.IsType<BadRequestObjectResult>(await campaigns.ConfirmPayment(offer.Id, new ConfirmPaymentRequest("", "", ""), CancellationToken.None));
        Assert.IsType<OkObjectResult>(await fees.Get(CancellationToken.None));
        Assert.IsType<OkObjectResult>(await fees.SetDefault(new SetOfferFeeRequest(70_000), CancellationToken.None));
        Assert.IsType<BadRequestObjectResult>(await fees.SetDefault(new SetOfferFeeRequest(null), CancellationToken.None));
        Assert.IsType<OkObjectResult>(await fees.SetStore(rig.A.Id, new SetOfferFeeRequest(null), CancellationToken.None));
        Assert.IsType<NotFoundObjectResult>(await fees.SetStore(Guid.NewGuid(), new SetOfferFeeRequest(5), CancellationToken.None));
        Assert.IsType<OkObjectResult>(await fees.Mine(CancellationToken.None));
    }
}

/// <summary>The same rules on SQL Server: one confirmation wins, an unpaid offer is never sent, refunds follow cancels, fees keep one row each.</summary>
public sealed class OfferFeeEfIntegrationTests
{
    private static string? ConnectionString => CheckoutEfIntegrationTests.ConnectionString;

    private static async Task<Guid> AddAsync(CheckoutEfIntegrationTests.World world, string title, int fee, PaymentState state)
    {
        await using var db = world.Db();
        var campaign = new Campaign { TitleEn = title, BodyEn = "body", StartsAt = DateTime.UtcNow.AddMinutes(-1), CreatedBy = "test", FeePaise = fee, PaymentStatus = state, Stores = [new CampaignStore { StoreId = world.Store.Id }] };
        db.Campaigns.Add(campaign);
        await db.SaveChangesAsync();
        return campaign.Id;
    }

    private static async Task<string> StartAsync(CheckoutEfIntegrationTests.World world, Guid campaignId)
    {
        await using var db = world.Db();
        var store = new EfCampaignPaymentStore(db);
        var payment = await store.BeginAttemptAsync(campaignId, DateTime.UtcNow);
        var orderId = "order_" + Guid.NewGuid().ToString("N")[..14];
        Assert.True(await store.AttachProviderOrderAsync(payment!.Id, orderId));
        return orderId;
    }

    private static async Task CleanAsync(CheckoutEfIntegrationTests.World world, params Guid[] ids)
    {
        await using var db = world.Db();
        await db.Notifications.Where(item => item.CampaignId != null && ids.Contains(item.CampaignId.Value)).ExecuteDeleteAsync();
        await db.Campaigns.Where(item => ids.Contains(item.Id)).ExecuteDeleteAsync();
    }

    [Fact]
    public async Task An_unpaid_offer_is_never_sent_and_once_paid_it_is_sent()
    {
        if (ConnectionString is null)
        {
            return;
        }

        await using var world = await CheckoutEfIntegrationTests.World.CreateAsync(customers: 1, stock: 5);
        var id = await AddAsync(world, "Fee offer " + Guid.NewGuid().ToString("N")[..6], 50_000, PaymentState.Created);
        try
        {
            await using (var db = world.Db())
            {
                Assert.Equal(0, await new EfCampaignStore(db).PublishDueCampaignsAsync(DateTime.UtcNow));
            }

            var orderId = await StartAsync(world, id);
            await using (var db = world.Db())
            {
                Assert.Equal(CapturedOutcome.Paid, await new EfCampaignPaymentStore(db).ApplyCapturedAsync(orderId, "pay_" + Guid.NewGuid().ToString("N")[..12], 50_000, DateTime.UtcNow));
            }

            await using (var db = world.Db())
            {
                Assert.True(await new EfCampaignStore(db).PublishDueCampaignsAsync(DateTime.UtcNow) >= 1);
            }

            await using var verify = world.Db();
            Assert.Contains(world.CustomerIds[0], await verify.Notifications.Where(item => item.CampaignId == id).Select(item => item.CustomerId).ToListAsync());
        }
        finally
        {
            await CleanAsync(world, id);
        }
    }

    [Fact]
    public async Task Confirming_from_several_places_at_once_pays_the_fee_once()
    {
        if (ConnectionString is null)
        {
            return;
        }

        await using var world = await CheckoutEfIntegrationTests.World.CreateAsync(customers: 1, stock: 5);
        var id = await AddAsync(world, "Race offer " + Guid.NewGuid().ToString("N")[..6], 30_000, PaymentState.Created);
        try
        {
            var orderId = await StartAsync(world, id);
            var paymentId = "pay_" + Guid.NewGuid().ToString("N")[..12];

            var outcomes = await Task.WhenAll(Enumerable.Range(0, 5).Select(async _ =>
            {
                await using var db = world.Db();
                return await new EfCampaignPaymentStore(db).ApplyCapturedAsync(orderId, paymentId, 30_000, DateTime.UtcNow);
            }));

            Assert.Equal(1, outcomes.Count(item => item == CapturedOutcome.Paid));
            Assert.All(outcomes, item => Assert.True(item is CapturedOutcome.Paid or CapturedOutcome.AlreadyPaid, item.ToString()));
            await using var verify = world.Db();
            Assert.Equal((PaymentState.Paid, PaymentState.Paid), ((await verify.CampaignPayments.AsNoTracking().SingleAsync(item => item.CampaignId == id)).Status, (await verify.Campaigns.AsNoTracking().SingleAsync(item => item.Id == id)).PaymentStatus));
        }
        finally
        {
            await CleanAsync(world, id);
        }
    }

    [Fact]
    public async Task A_payment_landing_while_the_offer_is_cancelled_ends_with_one_consistent_result()
    {
        if (ConnectionString is null)
        {
            return;
        }

        for (var round = 0; round < 4; round++)
        {
            await using var world = await CheckoutEfIntegrationTests.World.CreateAsync(customers: 1, stock: 5);
            var id = await AddAsync(world, "Cancel race " + Guid.NewGuid().ToString("N")[..6], 20_000, PaymentState.Created);
            try
            {
                var orderId = await StartAsync(world, id);
                var capture = Task.Run(async () =>
                {
                    await using var db = world.Db();
                    return await new EfCampaignPaymentStore(db).ApplyCapturedAsync(orderId, "pay_" + Guid.NewGuid().ToString("N")[..12], 20_000, DateTime.UtcNow);
                });
                var cancel = Task.Run(async () =>
                {
                    await using var db = world.Db();
                    return await new EfCampaignStore(db).TryCancelCampaignAsync(id);
                });
                await Task.WhenAll(capture, cancel);

                await using var verify = world.Db();
                var campaign = await verify.Campaigns.AsNoTracking().SingleAsync(item => item.Id == id);
                var payment = await verify.CampaignPayments.AsNoTracking().SingleAsync(item => item.CampaignId == id);
                Assert.False(campaign.IsActive);
                // Cancelled: a payment that got in first is queued for a refund, one that came later is refunded as late; an unpaid one is closed.
                Assert.True(payment.Status is PaymentState.Refunding or PaymentState.Failed, payment.Status.ToString());
                Assert.Equal(payment.Status, campaign.PaymentStatus);
            }
            finally
            {
                await CleanAsync(world, id);
            }
        }
    }

    [Fact]
    public async Task Fees_keep_one_default_row_and_one_row_per_store_and_a_second_payment_per_offer_is_refused()
    {
        if (ConnectionString is null)
        {
            return;
        }

        await using var world = await CheckoutEfIntegrationTests.World.CreateAsync(customers: 1, stock: 5);
        var id = await AddAsync(world, "Unique " + Guid.NewGuid().ToString("N")[..6], 10_000, PaymentState.Created);
        try
        {
            await using (var db = world.Db())
            {
                var fees = new EfOfferFeeStore(db);
                await Task.WhenAll(Enumerable.Range(0, 4).Select(async i =>
                {
                    await using var other = world.Db();
                    await new EfOfferFeeStore(other).SetAsync(world.Store.Id, 1000 + i, "test", DateTime.UtcNow);
                }));
                Assert.Equal(1, (await fees.GetOverridesAsync()).Keys.Count(key => key == world.Store.Id));
                await fees.SetAsync(world.Store.Id, null, "test", DateTime.UtcNow);
                Assert.DoesNotContain(world.Store.Id, (await fees.GetOverridesAsync()).Keys);
            }

            await StartAsync(world, id);
            await using var duplicate = world.Db();
            duplicate.CampaignPayments.Add(new CampaignPayment { CampaignId = id, AmountPaise = 10_000 });
            await Assert.ThrowsAsync<DbUpdateException>(() => duplicate.SaveChangesAsync());
            await using var zero = world.Db();
            zero.CampaignPayments.RemoveRange(await zero.CampaignPayments.Where(item => item.CampaignId == id).ToListAsync());
            zero.CampaignPayments.Add(new CampaignPayment { CampaignId = id, AmountPaise = 0 });
            await Assert.ThrowsAnyAsync<Exception>(() => zero.SaveChangesAsync());
        }
        finally
        {
            await using var db = world.Db();
            await db.OfferFees.Where(fee => fee.StoreId == world.Store.Id).ExecuteDeleteAsync();
            await CleanAsync(world, id);
        }
    }
}
