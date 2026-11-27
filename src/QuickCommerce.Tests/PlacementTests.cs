using System.Net;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using QuickCommerce.Api.Controllers;
using QuickCommerce.Application;
using QuickCommerce.Application.Caching;
using QuickCommerce.Application.DTOs;
using QuickCommerce.Application.Interfaces;
using QuickCommerce.Application.Services;
using QuickCommerce.Domain;
using QuickCommerce.Infrastructure;
using QuickCommerce.Infrastructure.Payments;
using QuickCommerce.Infrastructure.Persistence;
using Xunit;

namespace QuickCommerce.Tests;

/// <summary>Phase P15: stores bid for first place in a pincode for a week, pay up front, and the highest paid bid leads; beaten bids are refunded.</summary>
public sealed class PlacementTests
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

    private sealed class Rig
    {
        public required InMemoryCommerceStore Data { get; init; }
        public required TestClock Clock { get; init; }
        public required FakeRazorpay Razorpay { get; init; }
        public required PaymentSettings Settings { get; init; }
        public required Store A { get; init; }
        public required Store B { get; init; }
        public required Store C { get; init; }
        public required PlacementService Admin { get; init; }
        public required PlacementService StaffA { get; init; }
        public required PlacementService StaffB { get; init; }
        public required PlacementMaintenance Upkeep { get; init; }
    }

    // A Monday morning (UTC), so the next week starts on 19 October.
    private static readonly DateOnly NextWeek = new(2026, 10, 19);
    private static readonly DateOnly WeekAfter = new(2026, 10, 26);
    private const string Pin = "410209";

    private static Rig Create(bool paymentsOn = true)
    {
        var data = InMemoryCommerceStore.CreateSeeded();
        var stores = data.Stores.Take(3).ToArray();
        var organization = data.Organizations.Single().Id;
        var user = data.Users.Single().Id;
        var clock = new TestClock(new DateTimeOffset(2026, 10, 12, 4, 0, 0, TimeSpan.Zero));
        var settings = new PaymentSettings { Enabled = paymentsOn, KeyId = FakeRazorpay.KeyId, KeySecret = FakeRazorpay.KeySecret, WebhookSecret = FakeRazorpay.WebhookSecret };
        var razorpay = new FakeRazorpay();
        var gateway = new RazorpayGateway(new HttpClient(razorpay) { BaseAddress = new Uri(RazorpayGateway.BaseAddress) }, settings);
        PlacementService Make(bool admin, Store? store) => new(
            data,
            gateway,
            settings,
            new FixedScope(admin, organization),
            new FixedUser(admin ? new UserContext(user, organization, null, Role.Admin) : new UserContext(user, organization, store!.Id, Role.StoreStaff)),
            clock,
            NullLogger<PlacementService>.Instance);
        return new Rig
        {
            Data = data,
            Clock = clock,
            Razorpay = razorpay,
            Settings = settings,
            A = stores[0],
            B = stores[1],
            C = stores[2],
            Admin = Make(true, null),
            StaffA = Make(false, stores[0]),
            StaffB = Make(false, stores[1]),
            Upkeep = new PlacementMaintenance(data, gateway, settings, clock, NullLogger<PlacementMaintenance>.Instance)
        };
    }

    private static async Task<PlacementBidResponse> Bid(PlacementService who, long rupees, DateOnly? week = null, string pincode = Pin, Guid? storeId = null)
    {
        var result = await who.PlaceBidAsync(new PlaceBidRequest(pincode, week ?? NextWeek, rupees * 100, storeId));
        Assert.Equal(CampaignOperationStatus.Succeeded, result.Status);
        return result.Value!;
    }

    private static async Task<PaymentOperationResult<PlacementBidResponse>> Pay(Rig rig, PlacementService who, PlacementBidResponse bid, string status = "captured", long? amountPaise = null)
    {
        var start = (await who.StartPaymentAsync(bid.Id)).Value!;
        var paymentId = rig.Razorpay.PayOrder(start.ProviderOrderId, amountPaise ?? start.AmountPaise, status);
        return await who.ConfirmPaymentAsync(bid.Id, new ConfirmPaymentRequest(start.ProviderOrderId, paymentId, FakeRazorpay.Sign(start.ProviderOrderId, paymentId)));
    }

    private static async Task<PlacementBid> Row(Rig rig, Guid id) => (await rig.Data.GetBidAsync(id))!;

    // ---------------- rules and the market ----------------

    [Fact]
    public async Task The_rules_start_with_sensible_defaults_and_only_an_administrator_changes_them()
    {
        var rig = Create();

        var start = (await rig.StaffA.GetRulesAsync()).Value!;
        Assert.Equal((true, 50_000L, 10_000L, 4), (start.Enabled, start.MinBidPaise, start.MinStepPaise, start.MaxWeeksAhead));
        Assert.Equal(CampaignOperationStatus.Unauthorized, (await rig.StaffA.SetRulesAsync(new SetPlacementRulesRequest(true, 1, 1, 1))).Status);
        Assert.Equal(CampaignOperationStatus.InvalidRequest, (await rig.Admin.SetRulesAsync(new SetPlacementRulesRequest(true, 0, 1, 4))).Status);
        Assert.Equal(CampaignOperationStatus.InvalidRequest, (await rig.Admin.SetRulesAsync(new SetPlacementRulesRequest(true, 100, -1, 4))).Status);
        Assert.Equal(CampaignOperationStatus.InvalidRequest, (await rig.Admin.SetRulesAsync(new SetPlacementRulesRequest(true, 100, 1, 0))).Status);
        Assert.Equal(CampaignOperationStatus.InvalidRequest, (await rig.Admin.SetRulesAsync(new SetPlacementRulesRequest(true, 100, 1, 27))).Status);

        var changed = await rig.Admin.SetRulesAsync(new SetPlacementRulesRequest(true, 20_000, 5_000, 2));
        Assert.Equal((20_000L, 5_000L, 2), (changed.Value!.MinBidPaise, changed.Value.MinStepPaise, changed.Value.MaxWeeksAhead));
        Assert.Equal(2, (await rig.StaffA.GetMarketAsync(Pin)).Value!.Weeks.Count);
    }

    [Fact]
    public async Task The_market_shows_the_upcoming_weeks_with_what_leads_and_the_least_to_bid()
    {
        var rig = Create();
        var first = await Bid(rig.StaffA, 600);
        await Pay(rig, rig.StaffA, first);

        var market = (await rig.StaffA.GetMarketAsync(Pin)).Value!;
        var asB = (await rig.StaffB.GetMarketAsync(Pin)).Value!;

        Assert.Equal([NextWeek, WeekAfter, new DateOnly(2026, 11, 2), new DateOnly(2026, 11, 9)], market.Weeks.Select(week => week.WeekStart));
        Assert.Equal((60_000L, true, 70_000L), (market.Weeks[0].LeadingPaise, market.Weeks[0].LeadingIsMine, market.Weeks[0].MinNextBidPaise));
        Assert.Equal((null, false, 50_000L), (market.Weeks[1].LeadingPaise, market.Weeks[1].LeadingIsMine, market.Weeks[1].MinNextBidPaise));
        Assert.False(asB.Weeks[0].LeadingIsMine);
        Assert.Equal(new DateTime(2026, 10, 18, 18, 30, 0, DateTimeKind.Utc), market.Weeks[0].ClosesAt);
    }

    [Fact]
    public async Task While_paid_placement_is_off_nobody_can_bid_or_see_the_market()
    {
        var rig = Create();
        await rig.Admin.SetRulesAsync(new SetPlacementRulesRequest(false, 50_000, 10_000, 4));

        Assert.Equal(PlacementReasons.Disabled, (await rig.StaffA.GetMarketAsync(Pin)).Reason);
        Assert.Equal(PlacementReasons.Disabled, (await rig.StaffA.PlaceBidAsync(new PlaceBidRequest(Pin, NextWeek, 60_000))).Reason);
    }

    // ---------------- placing a bid ----------------

    [Fact]
    public async Task Store_staff_bid_for_their_own_store_and_an_administrator_for_any_store_they_name()
    {
        var rig = Create();

        var own = await Bid(rig.StaffA, 500);
        var forB = await Bid(rig.Admin, 500, WeekAfter, storeId: rig.B.Id);

        Assert.Equal((rig.A.Id, PlacementBidStatus.AwaitingPayment.ToString(), PaymentState.Created), (own.StoreId, own.Status, own.PaymentStatus));
        Assert.Equal(rig.B.Id, forB.StoreId);
        Assert.Equal(CampaignOperationStatus.Unauthorized, (await rig.StaffA.PlaceBidAsync(new PlaceBidRequest(Pin, NextWeek, 90_000, rig.B.Id))).Status);
        Assert.Equal(CampaignOperationStatus.InvalidRequest, (await rig.Admin.PlaceBidAsync(new PlaceBidRequest(Pin, NextWeek, 90_000))).Status);
        Assert.Equal(CampaignOperationStatus.InvalidRequest, (await rig.Admin.PlaceBidAsync(new PlaceBidRequest(Pin, NextWeek, 90_000, Guid.NewGuid()))).Status);
    }

    [Theory]
    [InlineData("41020")]
    [InlineData("0410209")]
    [InlineData("0123456")]
    [InlineData("41O209")]
    public async Task A_pincode_must_be_six_digits_not_starting_with_zero(string pincode)
    {
        var rig = Create();

        Assert.Equal(CampaignOperationStatus.InvalidRequest, (await rig.StaffA.PlaceBidAsync(new PlaceBidRequest(pincode, NextWeek, 60_000))).Status);
        Assert.Equal(CampaignOperationStatus.InvalidRequest, (await rig.StaffA.GetMarketAsync(pincode)).Status);
    }

    [Fact]
    public async Task A_bid_is_for_a_monday_in_an_open_week_with_an_amount_above_the_minimum_and_the_step()
    {
        var rig = Create();
        Assert.Equal(CampaignOperationStatus.InvalidRequest, (await rig.StaffA.PlaceBidAsync(new PlaceBidRequest(Pin, new DateOnly(2026, 10, 20), 60_000))).Status);
        Assert.Equal(CampaignOperationStatus.InvalidRequest, (await rig.StaffA.PlaceBidAsync(new PlaceBidRequest(Pin, NextWeek, 0))).Status);
        Assert.Equal(PlacementReasons.Closed, (await rig.StaffA.PlaceBidAsync(new PlaceBidRequest(Pin, new DateOnly(2026, 10, 12), 60_000))).Reason);
        Assert.Equal(PlacementReasons.Closed, (await rig.StaffA.PlaceBidAsync(new PlaceBidRequest(Pin, new DateOnly(2026, 10, 5), 60_000))).Reason);
        Assert.Equal(PlacementReasons.Closed, (await rig.StaffA.PlaceBidAsync(new PlaceBidRequest(Pin, new DateOnly(2026, 11, 16), 60_000))).Reason);
        Assert.Equal(PlacementReasons.TooLow, (await rig.StaffA.PlaceBidAsync(new PlaceBidRequest(Pin, NextWeek, 49_999))).Reason);
        Assert.Equal(CampaignOperationStatus.Succeeded, (await rig.StaffA.PlaceBidAsync(new PlaceBidRequest(Pin, NextWeek, 50_000))).Status);

        var leader = await Bid(rig.StaffB, 600);
        await Pay(rig, rig.StaffB, leader);
        Assert.Equal(PlacementReasons.TooLow, (await rig.StaffA.PlaceBidAsync(new PlaceBidRequest(Pin, NextWeek, 60_000))).Reason);
        Assert.Equal(PlacementReasons.TooLow, (await rig.StaffA.PlaceBidAsync(new PlaceBidRequest(Pin, NextWeek, 69_999))).Reason);
        Assert.Equal(CampaignOperationStatus.Succeeded, (await rig.StaffA.PlaceBidAsync(new PlaceBidRequest(Pin, NextWeek, 70_000))).Status);
    }

    [Fact]
    public async Task A_store_cannot_have_more_than_twenty_unpaid_bids_open()
    {
        var rig = Create();
        for (var i = 0; i < 20; i++)
        {
            await Bid(rig.StaffA, 500, pincode: $"4102{i:00}".Replace("4102", "4103"));
        }

        Assert.Equal(PlacementReasons.TooManyOpen, (await rig.StaffA.PlaceBidAsync(new PlaceBidRequest(Pin, NextWeek, 60_000))).Reason);
        Assert.Equal(CampaignOperationStatus.Succeeded, (await rig.StaffB.PlaceBidAsync(new PlaceBidRequest(Pin, NextWeek, 60_000))).Status);
    }

    // ---------------- paying and leading ----------------

    [Fact]
    public async Task A_paid_bid_leads_and_an_unpaid_one_never_does()
    {
        var rig = Create();
        var bid = await Bid(rig.StaffA, 600);
        Assert.Null(await rig.Data.GetLeadingStoreAsync(Pin, DateTime.UtcNow));
        Assert.Null((await rig.Data.GetLeadingAsync(Pin, NextWeek)));

        var paid = await Pay(rig, rig.StaffA, bid);

        Assert.Equal((PaymentOperationStatus.Succeeded, "Leading", PaymentState.Paid), (paid.Status, paid.Value!.Status, paid.Value.PaymentStatus));
        Assert.Equal(rig.A.Id, (await rig.Data.GetLeadingAsync(Pin, NextWeek))!.StoreId);
    }

    [Fact]
    public async Task A_higher_paid_bid_takes_the_lead_and_the_beaten_bid_is_refunded_in_full()
    {
        var rig = Create();
        var first = await Bid(rig.StaffA, 600);
        await Pay(rig, rig.StaffA, first);
        var second = await Bid(rig.StaffB, 700);

        var paid = await Pay(rig, rig.StaffB, second);

        Assert.Equal("Leading", paid.Value!.Status);
        var beaten = await Row(rig, first.Id);
        Assert.Equal((PlacementBidStatus.Outbid, PaymentState.Refunding), (beaten.Status, beaten.PaymentStatus));
        Assert.Equal(rig.B.Id, (await rig.Data.GetLeadingAsync(Pin, NextWeek))!.StoreId);
        Assert.Equal(1, await rig.Upkeep.StartRefundsAsync());
        Assert.Equal(PaymentState.Refunded, (await Row(rig, first.Id)).PaymentStatus);
        Assert.Equal(PaymentState.Paid, (await Row(rig, second.Id)).PaymentStatus);
        var refund = rig.Razorpay.Requests.Single(request => request.Path.EndsWith("/refund", StringComparison.Ordinal));
        Assert.Equal($"v1/payments/{beaten.ProviderPaymentId}/refund", refund.Path);
    }

    [Fact]
    public async Task A_bid_paid_after_a_higher_one_is_already_leading_is_refunded_and_never_leads()
    {
        var rig = Create();
        var low = await Bid(rig.StaffA, 600);
        var high = await Bid(rig.StaffB, 700);
        await Pay(rig, rig.StaffB, high);

        var late = await Pay(rig, rig.StaffA, low);

        Assert.Equal(PlacementReasons.Outbid, late.Reason);
        var row = await Row(rig, low.Id);
        Assert.Equal((PlacementBidStatus.Outbid, PaymentState.Refunding), (row.Status, row.PaymentStatus));
        Assert.Equal(rig.B.Id, (await rig.Data.GetLeadingAsync(Pin, NextWeek))!.StoreId);
        Assert.Equal(1, await rig.Upkeep.StartRefundsAsync());
    }

    [Fact]
    public async Task Equal_bids_are_won_by_whoever_paid_first()
    {
        var rig = Create();
        var first = await Bid(rig.StaffA, 600);
        var second = await Bid(rig.StaffB, 600);
        await Pay(rig, rig.StaffA, first);

        var tied = await Pay(rig, rig.StaffB, second);

        Assert.Equal(PlacementReasons.Outbid, tied.Reason);
        Assert.Equal(rig.A.Id, (await rig.Data.GetLeadingAsync(Pin, NextWeek))!.StoreId);
    }

    [Fact]
    public async Task A_store_raising_its_own_bid_replaces_its_earlier_one_which_is_refunded()
    {
        var rig = Create();
        var first = await Bid(rig.StaffA, 600);
        await Pay(rig, rig.StaffA, first);
        var raised = await Bid(rig.StaffA, 700);

        await Pay(rig, rig.StaffA, raised);

        Assert.Equal(PlacementBidStatus.Outbid, (await Row(rig, first.Id)).Status);
        Assert.Equal(raised.Id, (await rig.Data.GetLeadingAsync(Pin, NextWeek))!.Id);
    }

    [Fact]
    public async Task Different_pincodes_and_weeks_have_their_own_leaders()
    {
        var rig = Create();
        await Pay(rig, rig.StaffA, await Bid(rig.StaffA, 600));
        await Pay(rig, rig.StaffB, await Bid(rig.StaffB, 500, WeekAfter));
        await Pay(rig, rig.StaffB, await Bid(rig.StaffB, 500, pincode: "410210"));

        Assert.Equal(rig.A.Id, (await rig.Data.GetLeadingAsync(Pin, NextWeek))!.StoreId);
        Assert.Equal(rig.B.Id, (await rig.Data.GetLeadingAsync(Pin, WeekAfter))!.StoreId);
        Assert.Equal(rig.B.Id, (await rig.Data.GetLeadingAsync("410210", NextWeek))!.StoreId);
    }

    // ---------------- payment details ----------------

    [Fact]
    public async Task Starting_gives_the_checkout_exactly_the_bid_amount_and_asking_again_never_makes_a_second_provider_order()
    {
        var rig = Create();
        var bid = await Bid(rig.StaffA, 600);

        var first = (await rig.StaffA.StartPaymentAsync(bid.Id)).Value!;
        var second = (await rig.StaffA.StartPaymentAsync(bid.Id)).Value!;

        Assert.Equal((60_000L, "INR", FakeRazorpay.KeyId), (first.AmountPaise, first.Currency, first.KeyId));
        Assert.Equal(first.ProviderOrderId, second.ProviderOrderId);
        Assert.Equal(1, rig.Razorpay.Count(HttpMethod.Post, "v1/orders"));
        Assert.DoesNotContain(FakeRazorpay.KeySecret, System.Text.Json.JsonSerializer.Serialize(first));
    }

    [Fact]
    public async Task Other_stores_cannot_see_or_pay_a_bid_and_an_administrator_can()
    {
        var rig = Create();
        var bid = await Bid(rig.StaffA, 600);

        Assert.Equal(PaymentOperationStatus.NotFound, (await rig.StaffB.StartPaymentAsync(bid.Id)).Status);
        Assert.Equal(PaymentOperationStatus.NotFound, (await rig.StaffB.ConfirmPaymentAsync(bid.Id, new ConfirmPaymentRequest("o", "p", "s"))).Status);
        Assert.Equal(CampaignOperationStatus.NotFound, (await rig.StaffB.CancelBidAsync(bid.Id)).Status);
        Assert.Empty((await rig.StaffB.ListBidsAsync()).Value!);
        Assert.Single((await rig.StaffA.ListBidsAsync()).Value!);
        Assert.Single((await rig.Admin.ListBidsAsync()).Value!);
        Assert.Equal(PaymentOperationStatus.Succeeded, (await rig.Admin.StartPaymentAsync(bid.Id)).Status);
    }

    [Theory]
    [InlineData("")]
    [InlineData("not-a-signature")]
    [InlineData("0000000000000000000000000000000000000000000000000000000000000000")]
    public async Task A_made_up_signature_never_pays_a_bid(string signature)
    {
        var rig = Create();
        var bid = await Bid(rig.StaffA, 600);
        var start = (await rig.StaffA.StartPaymentAsync(bid.Id)).Value!;
        var paymentId = rig.Razorpay.PayOrder(start.ProviderOrderId, start.AmountPaise);

        var result = await rig.StaffA.ConfirmPaymentAsync(bid.Id, new ConfirmPaymentRequest(start.ProviderOrderId, paymentId, signature));

        Assert.NotEqual(PaymentOperationStatus.Succeeded, result.Status);
        Assert.Equal(PlacementBidStatus.AwaitingPayment, (await Row(rig, bid.Id)).Status);
    }

    [Fact]
    public async Task A_smaller_payment_and_an_uncaptured_one_do_not_lead_and_confirming_twice_changes_nothing()
    {
        var rig = Create();
        var bid = await Bid(rig.StaffA, 600);
        var uncaptured = await Pay(rig, rig.StaffA, bid, status: "authorized");
        Assert.Equal(PaymentReasons.NotCaptured, uncaptured.Reason);
        var small = await Pay(rig, rig.StaffA, bid, amountPaise: 59_900);
        Assert.Equal(PaymentReasons.AmountMismatch, small.Reason);
        Assert.Equal((PlacementBidStatus.Cancelled, PaymentState.Refunding), ((await Row(rig, bid.Id)).Status, (await Row(rig, bid.Id)).PaymentStatus));

        var good = await Bid(rig.StaffB, 600);
        var first = await Pay(rig, rig.StaffB, good);
        var start = (await rig.StaffB.StartPaymentAsync(good.Id)).Status;

        Assert.Equal(PaymentOperationStatus.Succeeded, first.Status);
        Assert.Equal(PaymentOperationStatus.Conflict, start);
        Assert.Equal(rig.B.Id, (await rig.Data.GetLeadingAsync(Pin, NextWeek))!.StoreId);
    }

    [Fact]
    public async Task If_the_provider_is_down_the_payment_is_settled_by_the_lookup_and_starting_works_on_the_next_try()
    {
        var rig = Create();
        var bid = await Bid(rig.StaffA, 600);
        rig.Razorpay.Down = true;
        Assert.Equal(PaymentReasons.ProviderUnavailable, (await rig.StaffA.StartPaymentAsync(bid.Id)).Reason);
        rig.Razorpay.Down = false;
        var start = (await rig.StaffA.StartPaymentAsync(bid.Id)).Value!;
        var paymentId = rig.Razorpay.PayOrder(start.ProviderOrderId, start.AmountPaise);
        rig.Razorpay.Down = true;
        var down = await rig.StaffA.ConfirmPaymentAsync(bid.Id, new ConfirmPaymentRequest(start.ProviderOrderId, paymentId, FakeRazorpay.Sign(start.ProviderOrderId, paymentId)));
        rig.Razorpay.Down = false;
        rig.Clock.Advance(TimeSpan.FromMinutes(30));

        Assert.Equal(PaymentReasons.ProviderUnavailable, down.Reason);
        Assert.Equal(1, await rig.Upkeep.ReconcileAsync());
        Assert.Equal((PlacementBidStatus.Leading, PaymentState.Paid), ((await Row(rig, bid.Id)).Status, (await Row(rig, bid.Id)).PaymentStatus));
    }

    [Fact]
    public async Task While_online_payment_is_off_a_bid_can_be_placed_but_not_paid()
    {
        var rig = Create(paymentsOn: false);
        var bid = await Bid(rig.StaffA, 600);

        Assert.Equal(PaymentReasons.PaymentsUnavailable, (await rig.StaffA.StartPaymentAsync(bid.Id)).Reason);
        Assert.Equal(PaymentReasons.PaymentsUnavailable, (await rig.StaffA.ConfirmPaymentAsync(bid.Id, new ConfirmPaymentRequest("o", "p", "s"))).Reason);
    }

    // ---------------- closing and cancelling ----------------

    [Fact]
    public async Task Bidding_closes_when_the_week_starts_and_a_payment_landing_after_that_is_refunded()
    {
        var rig = Create();
        var bid = await Bid(rig.StaffA, 600);
        var start = (await rig.StaffA.StartPaymentAsync(bid.Id)).Value!;
        var paymentId = rig.Razorpay.PayOrder(start.ProviderOrderId, start.AmountPaise);
        rig.Clock.Now = new DateTimeOffset(2026, 10, 18, 18, 30, 0, TimeSpan.Zero); // Monday 00:00 India time

        var late = await rig.StaffA.ConfirmPaymentAsync(bid.Id, new ConfirmPaymentRequest(start.ProviderOrderId, paymentId, FakeRazorpay.Sign(start.ProviderOrderId, paymentId)));

        Assert.Equal(PlacementReasons.Closed, late.Reason);
        Assert.Equal((PlacementBidStatus.Cancelled, PaymentState.Refunding), ((await Row(rig, bid.Id)).Status, (await Row(rig, bid.Id)).PaymentStatus));
        Assert.Equal(PlacementReasons.Closed, (await rig.StaffA.StartPaymentAsync(bid.Id)).Reason);
        Assert.Equal(PlacementReasons.Closed, (await rig.StaffB.PlaceBidAsync(new PlaceBidRequest(Pin, NextWeek, 90_000))).Reason);
        Assert.Equal(1, await rig.Upkeep.StartRefundsAsync());
    }

    [Fact]
    public async Task A_store_withdraws_an_unpaid_bid_but_not_a_paid_one_and_an_administrator_cancels_a_paid_one_with_a_refund()
    {
        var rig = Create();
        var unpaid = await Bid(rig.StaffA, 500, WeekAfter);
        var paid = await Bid(rig.StaffA, 600);
        await Pay(rig, rig.StaffA, paid);

        var withdrawn = await rig.StaffA.CancelBidAsync(unpaid.Id);
        var refused = await rig.StaffA.CancelBidAsync(paid.Id);
        var cancelled = await rig.Admin.CancelBidAsync(paid.Id);

        Assert.Equal(("Cancelled", PaymentState.Failed), (withdrawn.Value!.Status, withdrawn.Value.PaymentStatus));
        Assert.Equal(CampaignOperationStatus.Conflict, refused.Status);
        Assert.Equal(("Cancelled", PaymentState.Refunding), (cancelled.Value!.Status, cancelled.Value.PaymentStatus));
        Assert.Null(await rig.Data.GetLeadingAsync(Pin, NextWeek));
        Assert.Equal(1, await rig.Upkeep.StartRefundsAsync());
        Assert.Equal(PaymentState.Refunded, (await Row(rig, paid.Id)).PaymentStatus);
        Assert.Equal(CampaignOperationStatus.Succeeded, (await rig.Admin.CancelBidAsync(paid.Id)).Status); // twice is fine
    }

    [Fact]
    public async Task A_refund_the_provider_refuses_for_good_is_marked_failed_and_a_busy_provider_keeps_it_queued()
    {
        var rig = Create();
        var bid = await Bid(rig.StaffA, 600);
        await Pay(rig, rig.StaffA, bid);
        await rig.Admin.CancelBidAsync(bid.Id);
        rig.Razorpay.FailNext = (HttpStatusCode.ServiceUnavailable, "{}");
        Assert.Equal(0, await rig.Upkeep.StartRefundsAsync());
        Assert.Equal(PaymentState.Refunding, (await Row(rig, bid.Id)).PaymentStatus);

        rig.Razorpay.FailNext = (HttpStatusCode.BadRequest, """{"error":{"code":"BAD_REQUEST_ERROR","description":"The refund amount is invalid"}}""");
        await rig.Upkeep.StartRefundsAsync();

        Assert.Equal(PaymentState.RefundFailed, (await Row(rig, bid.Id)).PaymentStatus);
        Assert.Equal(0, await rig.Upkeep.StartRefundsAsync());
    }

    [Fact]
    public async Task A_refund_still_processing_is_closed_by_the_lookup()
    {
        var rig = Create();
        rig.Razorpay.RefundStatus = "pending";
        var bid = await Bid(rig.StaffA, 600);
        await Pay(rig, rig.StaffA, bid);
        await rig.Admin.CancelBidAsync(bid.Id);
        await rig.Upkeep.StartRefundsAsync();
        var row = await Row(rig, bid.Id);
        rig.Razorpay.Payments[row.ProviderPaymentId!] = rig.Razorpay.Payments[row.ProviderPaymentId!] with { Refunded = row.AmountPaise };
        rig.Clock.Advance(TimeSpan.FromMinutes(30));

        Assert.Equal(1, await rig.Upkeep.ReconcileAsync());
        Assert.Equal(PaymentState.Refunded, (await Row(rig, bid.Id)).PaymentStatus);
    }

    // ---------------- weeks and routes ----------------

    [Fact]
    public void Weeks_start_on_monday_in_india_time_and_a_pincode_is_six_digits()
    {
        Assert.Equal(new DateOnly(2026, 10, 12), PlacementWeeks.WeekStartOf(new DateTime(2026, 10, 12, 0, 0, 0, DateTimeKind.Utc)));
        Assert.Equal(new DateOnly(2026, 10, 12), PlacementWeeks.WeekStartOf(new DateTime(2026, 10, 18, 18, 29, 0, DateTimeKind.Utc))); // Sunday 23:59 India
        Assert.Equal(new DateOnly(2026, 10, 19), PlacementWeeks.WeekStartOf(new DateTime(2026, 10, 18, 18, 30, 0, DateTimeKind.Utc))); // Monday 00:00 India
        Assert.Equal(new DateOnly(2026, 10, 5), PlacementWeeks.WeekStartOf(new DateTime(2026, 10, 11, 18, 0, 0, DateTimeKind.Utc)));
        Assert.Equal(new DateTime(2026, 10, 18, 18, 30, 0, DateTimeKind.Utc), PlacementWeeks.StartUtc(new DateOnly(2026, 10, 19)));
        Assert.True(PlacementWeeks.IsPincode("410209"));
        Assert.False(PlacementWeeks.IsPincode(null));
        Assert.False(PlacementWeeks.IsPincode("041020"));
    }

    [Fact]
    public async Task The_routes_answer_with_the_right_codes()
    {
        var rig = Create();
        var controller = new PlacementController(rig.StaffA) { ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext() } };

        Assert.IsType<OkObjectResult>(await controller.Rules(CancellationToken.None));
        Assert.IsType<OkObjectResult>(await controller.Market(Pin, CancellationToken.None));
        Assert.IsType<BadRequestObjectResult>(await controller.Market("12", CancellationToken.None));
        var created = Assert.IsType<CreatedResult>(await controller.PlaceBid(new PlaceBidRequest(Pin, NextWeek, 60_000), CancellationToken.None));
        Assert.IsType<ConflictObjectResult>(await controller.PlaceBid(new PlaceBidRequest(Pin, NextWeek, 100), CancellationToken.None));
        var id = ((PlacementBidResponse)created.Value!.GetType().GetProperty("data")!.GetValue(created.Value)!).Id;
        Assert.IsType<OkObjectResult>(await controller.StartPayment(id, CancellationToken.None));
        Assert.IsType<NotFoundObjectResult>(await controller.StartPayment(Guid.NewGuid(), CancellationToken.None));
        Assert.IsType<ConflictObjectResult>(await controller.ConfirmPayment(id, new ConfirmPaymentRequest("o", "p", "s"), CancellationToken.None));
        Assert.IsType<OkObjectResult>(await controller.Bids(CancellationToken.None));
        Assert.IsType<OkObjectResult>(await controller.Cancel(id, CancellationToken.None));
    }

    // ---------------- the customer's list ----------------

    private sealed class NoCache : ICacheService
    {
        public Task<T?> GetAsync<T>(string key, CancellationToken cancellationToken = default) => Task.FromResult(default(T));
        public Task SetAsync<T>(string key, T value, TimeSpan ttl, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task RemoveAsync(string key, CancellationToken cancellationToken = default) => Task.CompletedTask;
    }

    private static (CatalogService Service, InMemoryCommerceStore Data) CatalogWith(Rig rig, PlacementRules? rules = null)
    {
        var service = new CatalogService(rig.Data, new StoreSelectionService(), new NoCache(), new DeliveryEstimator(new DeliverySettings()), rig.Data, rig.Clock);
        return (service, rig.Data);
    }

    [Fact]
    public async Task The_store_that_leads_the_pincode_this_week_is_listed_first_and_marked_sponsored_the_rest_stay_nearest_first()
    {
        var rig = Create();
        var (service, data) = CatalogWith(rig);
        var harbor = data.Stores.Single(store => store.Name == "Harbor Point Dark Store");
        var plain = (await service.GetServiceableStoresAsync(new NearestStoreQuery(harbor.Latitude, harbor.Longitude))).Stores;
        var farthest = plain[^1];
        Assert.True(plain.Count > 1);
        // Win this week's slot for the farthest store (the leading bid is for the current week, as it would be once that week has begun).
        data.PlacementBids.Add(new PlacementBid { StoreId = farthest.Id, Pincode = Pin, WeekStart = new DateOnly(2026, 10, 12), AmountPaise = 60_000, Status = PlacementBidStatus.Leading, PaymentStatus = PaymentState.Paid, Store = data.Stores.Single(store => store.Id == farthest.Id) });

        var result = (await service.GetServiceableStoresAsync(new NearestStoreQuery(harbor.Latitude, harbor.Longitude, Pin))).Stores;

        Assert.Equal(farthest.Id, result[0].Id);
        Assert.True(result[0].Sponsored);
        Assert.All(result.Skip(1), store => Assert.False(store.Sponsored));
        Assert.Equal(plain.Where(store => store.Id != farthest.Id).Select(store => store.Id), result.Skip(1).Select(store => store.Id));
    }

    [Fact]
    public async Task Without_a_pincode_with_another_pincode_with_a_bad_one_or_when_placement_is_off_the_list_is_unchanged()
    {
        var rig = Create();
        var (service, data) = CatalogWith(rig);
        var harbor = data.Stores.Single(store => store.Name == "Harbor Point Dark Store");
        var plain = (await service.GetServiceableStoresAsync(new NearestStoreQuery(harbor.Latitude, harbor.Longitude))).Stores;
        var farthest = plain[^1];
        data.PlacementBids.Add(new PlacementBid { StoreId = farthest.Id, Pincode = Pin, WeekStart = new DateOnly(2026, 10, 12), AmountPaise = 60_000, Status = PlacementBidStatus.Leading, PaymentStatus = PaymentState.Paid, Store = data.Stores.Single(store => store.Id == farthest.Id) });

        foreach (var pincode in new string?[] { null, "999999", "abc", "12" })
        {
            var result = (await service.GetServiceableStoresAsync(new NearestStoreQuery(harbor.Latitude, harbor.Longitude, pincode))).Stores;
            Assert.Equal(plain.Select(store => store.Id), result.Select(store => store.Id));
            Assert.All(result, store => Assert.False(store.Sponsored));
        }

        await rig.Admin.SetRulesAsync(new SetPlacementRulesRequest(false, 50_000, 10_000, 4));
        var off = (await service.GetServiceableStoresAsync(new NearestStoreQuery(harbor.Latitude, harbor.Longitude, Pin))).Stores;
        Assert.Equal(plain.Select(store => store.Id), off.Select(store => store.Id));
    }

    [Fact]
    public async Task A_leader_that_does_not_deliver_to_the_customer_is_not_shown_and_next_weeks_leader_is_not_this_weeks()
    {
        var rig = Create();
        var (service, data) = CatalogWith(rig);
        var harbor = data.Stores.Single(store => store.Name == "Harbor Point Dark Store");
        var plain = (await service.GetServiceableStoresAsync(new NearestStoreQuery(harbor.Latitude, harbor.Longitude))).Stores;
        // The farthest store is switched off, so it no longer delivers here, yet it leads the pincode this week: it must not appear.
        var nonDelivering = data.Stores.Single(store => store.Id == plain[^1].Id);
        nonDelivering.IsActive = false;
        plain = (await service.GetServiceableStoresAsync(new NearestStoreQuery(harbor.Latitude, harbor.Longitude))).Stores;
        data.PlacementBids.Add(new PlacementBid { StoreId = nonDelivering.Id, Pincode = Pin, WeekStart = new DateOnly(2026, 10, 12), AmountPaise = 60_000, Status = PlacementBidStatus.Leading, PaymentStatus = PaymentState.Paid, Store = nonDelivering });
        data.PlacementBids.Add(new PlacementBid { StoreId = plain[^1].Id, Pincode = Pin, WeekStart = NextWeek, AmountPaise = 60_000, Status = PlacementBidStatus.Leading, PaymentStatus = PaymentState.Paid, Store = data.Stores.Single(store => store.Id == plain[^1].Id) });

        var result = (await service.GetServiceableStoresAsync(new NearestStoreQuery(harbor.Latitude, harbor.Longitude, Pin))).Stores;

        Assert.Equal(plain.Select(store => store.Id), result.Select(store => store.Id));
        Assert.All(result, store => Assert.False(store.Sponsored));
    }
}

/// <summary>The same rules on SQL Server: one leader per pincode and week, parallel payments, and the outbid refund.</summary>
[Collection("SqlServer")]
public sealed class PlacementEfIntegrationTests
{
    private static string? ConnectionString => CheckoutEfIntegrationTests.ConnectionString;

    // Weeks far in the future so the tests never clash with real bids; the pincode is random per test.
    private static readonly DateOnly Week = new(2036, 3, 3);

    private static string NewPincode() => "4" + Random.Shared.Next(10000, 99999);

    private static async Task<Guid> BidAsync(CheckoutEfIntegrationTests.World world, string pincode, long paise, DateOnly? week = null)
    {
        await using var db = world.Db();
        var bid = new PlacementBid { StoreId = world.Store.Id, Pincode = pincode, WeekStart = week ?? Week, AmountPaise = paise };
        db.PlacementBids.Add(bid);
        await db.SaveChangesAsync();
        return bid.Id;
    }

    private static async Task<string> OrderAsync(CheckoutEfIntegrationTests.World world, Guid bidId)
    {
        await using var db = world.Db();
        var store = new EfPlacementStore(db);
        await store.BeginAttemptAsync(bidId, DateTime.UtcNow);
        var orderId = "order_" + Guid.NewGuid().ToString("N")[..14];
        Assert.True(await store.AttachProviderOrderAsync(bidId, orderId));
        return orderId;
    }

    private static async Task<PlacementCaptureOutcome> CaptureAsync(CheckoutEfIntegrationTests.World world, string orderId, long paise)
    {
        await using var db = world.Db();
        return await new EfPlacementStore(db).ApplyCapturedAsync(orderId, "pay_" + Guid.NewGuid().ToString("N")[..12], paise, new DateTime(2036, 2, 1, 0, 0, 0, DateTimeKind.Utc));
    }

    private static async Task CleanAsync(CheckoutEfIntegrationTests.World world)
    {
        await using var db = world.Db();
        await db.PlacementBids.Where(bid => bid.StoreId == world.Store.Id).ExecuteDeleteAsync();
    }

    [Fact]
    public async Task A_higher_paid_bid_takes_the_lead_and_the_beaten_one_is_queued_for_a_refund()
    {
        if (ConnectionString is null)
        {
            return;
        }

        await using var world = await CheckoutEfIntegrationTests.World.CreateAsync(customers: 1, stock: 5);
        var pincode = NewPincode();
        try
        {
            var low = await BidAsync(world, pincode, 60_000);
            var high = await BidAsync(world, pincode, 70_000);
            Assert.Equal(PlacementCaptureOutcome.Leading, await CaptureAsync(world, await OrderAsync(world, low), 60_000));
            Assert.Equal(PlacementCaptureOutcome.Leading, await CaptureAsync(world, await OrderAsync(world, high), 70_000));

            await using var verify = world.Db();
            var rows = await verify.PlacementBids.AsNoTracking().Where(bid => bid.Pincode == pincode).ToListAsync();
            Assert.Equal((PlacementBidStatus.Outbid, PaymentState.Refunding), (rows.Single(bid => bid.Id == low).Status, rows.Single(bid => bid.Id == low).PaymentStatus));
            Assert.Equal((PlacementBidStatus.Leading, PaymentState.Paid), (rows.Single(bid => bid.Id == high).Status, rows.Single(bid => bid.Id == high).PaymentStatus));
            Assert.Equal(world.Store.Id, await new EfPlacementStore(verify).GetLeadingStoreAsync(pincode, PlacementWeeks.StartUtc(Week).AddDays(1)));
        }
        finally
        {
            await CleanAsync(world);
        }
    }

    [Fact]
    public async Task A_lower_bid_paid_after_a_higher_one_is_refunded_and_equal_bids_go_to_the_first_to_pay()
    {
        if (ConnectionString is null)
        {
            return;
        }

        await using var world = await CheckoutEfIntegrationTests.World.CreateAsync(customers: 1, stock: 5);
        var pincode = NewPincode();
        try
        {
            var high = await BidAsync(world, pincode, 70_000);
            var low = await BidAsync(world, pincode, 60_000);
            var tie = await BidAsync(world, pincode, 70_000);
            Assert.Equal(PlacementCaptureOutcome.Leading, await CaptureAsync(world, await OrderAsync(world, high), 70_000));
            Assert.Equal(PlacementCaptureOutcome.Outbid, await CaptureAsync(world, await OrderAsync(world, low), 60_000));
            Assert.Equal(PlacementCaptureOutcome.Outbid, await CaptureAsync(world, await OrderAsync(world, tie), 70_000));

            await using var verify = world.Db();
            Assert.Equal(1, await verify.PlacementBids.CountAsync(bid => bid.Pincode == pincode && bid.Status == PlacementBidStatus.Leading));
            Assert.Equal(high, (await verify.PlacementBids.AsNoTracking().SingleAsync(bid => bid.Pincode == pincode && bid.Status == PlacementBidStatus.Leading)).Id);
        }
        finally
        {
            await CleanAsync(world);
        }
    }

    [Fact]
    public async Task Bids_paid_at_the_same_moment_leave_exactly_one_leader_the_highest()
    {
        if (ConnectionString is null)
        {
            return;
        }

        await using var world = await CheckoutEfIntegrationTests.World.CreateAsync(customers: 1, stock: 5);
        var pincode = NewPincode();
        try
        {
            var amounts = new long[] { 60_000, 70_000, 80_000, 90_000, 100_000 };
            var orders = new List<(string Order, long Paise)>();
            foreach (var paise in amounts)
            {
                orders.Add((await OrderAsync(world, await BidAsync(world, pincode, paise)), paise));
            }

            var outcomes = await Task.WhenAll(orders.Select(item => CaptureAsync(world, item.Order, item.Paise)));

            await using var verify = world.Db();
            var rows = await verify.PlacementBids.AsNoTracking().Where(bid => bid.Pincode == pincode).ToListAsync();
            Assert.Equal(1, rows.Count(bid => bid.Status == PlacementBidStatus.Leading));
            Assert.Equal(100_000, rows.Single(bid => bid.Status == PlacementBidStatus.Leading).AmountPaise);
            Assert.All(rows.Where(bid => bid.Status != PlacementBidStatus.Leading), bid => Assert.Equal((PlacementBidStatus.Outbid, PaymentState.Refunding), (bid.Status, bid.PaymentStatus)));
            Assert.All(outcomes, outcome => Assert.True(outcome is PlacementCaptureOutcome.Leading or PlacementCaptureOutcome.Outbid, outcome.ToString()));
        }
        finally
        {
            await CleanAsync(world);
        }
    }

    [Fact]
    public async Task The_database_refuses_two_leaders_for_one_pincode_and_week_and_a_zero_amount()
    {
        if (ConnectionString is null)
        {
            return;
        }

        await using var world = await CheckoutEfIntegrationTests.World.CreateAsync(customers: 1, stock: 5);
        var pincode = NewPincode();
        try
        {
            await using (var db = world.Db())
            {
                db.PlacementBids.Add(new PlacementBid { StoreId = world.Store.Id, Pincode = pincode, WeekStart = Week, AmountPaise = 60_000, Status = PlacementBidStatus.Leading, PaymentStatus = PaymentState.Paid });
                await db.SaveChangesAsync();
            }

            await using var second = world.Db();
            second.PlacementBids.Add(new PlacementBid { StoreId = world.Store.Id, Pincode = pincode, WeekStart = Week, AmountPaise = 70_000, Status = PlacementBidStatus.Leading, PaymentStatus = PaymentState.Paid });
            await Assert.ThrowsAsync<DbUpdateException>(() => second.SaveChangesAsync());

            await using var zero = world.Db();
            zero.PlacementBids.Add(new PlacementBid { StoreId = world.Store.Id, Pincode = pincode, WeekStart = Week.AddDays(7), AmountPaise = 0 });
            await Assert.ThrowsAsync<DbUpdateException>(() => zero.SaveChangesAsync());
        }
        finally
        {
            await CleanAsync(world);
        }
    }

    [Fact]
    public async Task The_rules_are_saved_and_read_back_and_a_cancelled_paid_bid_is_queued_for_a_refund()
    {
        if (ConnectionString is null)
        {
            return;
        }

        await using var world = await CheckoutEfIntegrationTests.World.CreateAsync(customers: 1, stock: 5);
        var pincode = NewPincode();
        PlacementRules? before = null;
        try
        {
            await using (var db = world.Db())
            {
                before = await new EfPlacementStore(db).GetRulesAsync();
            }

            var bid = await BidAsync(world, pincode, 60_000);
            await CaptureAsync(world, await OrderAsync(world, bid), 60_000);
            await using (var db = world.Db())
            {
                var cancelled = await new EfPlacementStore(db).CancelBidAsync(bid, DateTime.UtcNow);
                Assert.Equal((PlacementBidStatus.Cancelled, PaymentState.Refunding), (cancelled!.Status, cancelled.PaymentStatus));
                Assert.Contains(bid, (await new EfPlacementStore(db).GetRefundsToStartAsync(500)).Select(item => item.Id));
            }
        }
        finally
        {
            await CleanAsync(world);
            await using var db = world.Db();
            if (before is not null)
            {
                await new EfPlacementStore(db).SetRulesAsync(before);
            }
        }
    }
}
