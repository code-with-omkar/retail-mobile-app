using Microsoft.Extensions.Logging;
using QuickCommerce.Application.Interfaces;
using QuickCommerce.Domain;

namespace QuickCommerce.Application.Services;

/// <summary>What happens to a bid and the bid it beats at each payment event: pure rules, shared by the SQL and in-memory stores.</summary>
public static class PlacementTransitions
{
    public static bool Settled(PlacementBid bid) => bid.PaymentStatus is PaymentState.Paid or PaymentState.Refunding or PaymentState.Refunded or PaymentState.RefundFailed;

    /// <summary>
    /// The provider says a bid was paid. It leads when it is higher than the current leader (which is then outbid and refunded); a bid
    /// that is not higher, or that arrives after its week started or after it was cancelled, is refunded instead.
    /// </summary>
    public static PlacementCaptureOutcome ApplyCaptured(PlacementBid bid, PlacementBid? current, string providerPaymentId, long paidPaise, DateTime now)
    {
        if (Settled(bid))
        {
            return PlacementCaptureOutcome.AlreadyPaid;
        }

        bid.ProviderPaymentId = providerPaymentId;
        bid.UpdatedAt = now;
        if (paidPaise != bid.AmountPaise)
        {
            Refund(bid, PlacementBidStatus.Cancelled, "AmountMismatch");
            return PlacementCaptureOutcome.AmountMismatch;
        }

        if (bid.Status == PlacementBidStatus.Cancelled || now >= PlacementWeeks.StartUtc(bid.WeekStart))
        {
            Refund(bid, PlacementBidStatus.Cancelled, "Late");
            return PlacementCaptureOutcome.Late;
        }

        if (current is null || bid.AmountPaise > current.AmountPaise)
        {
            if (current is not null)
            {
                current.Status = PlacementBidStatus.Outbid;
                current.PaymentStatus = PaymentState.Refunding;
                current.FailureReason = "Outbid";
                current.UpdatedAt = now;
            }

            bid.Status = PlacementBidStatus.Leading;
            bid.PaymentStatus = PaymentState.Paid;
            bid.FailureReason = null;
            return PlacementCaptureOutcome.Leading;
        }

        Refund(bid, PlacementBidStatus.Outbid, "Outbid");
        return PlacementCaptureOutcome.Outbid;
    }

    private static void Refund(PlacementBid bid, PlacementBidStatus status, string reason)
    {
        bid.Status = status;
        bid.PaymentStatus = PaymentState.Refunding;
        bid.FailureReason = reason;
    }

    /// <summary>A bid is cancelled: unpaid is closed, paid (leading) is queued for a full refund.</summary>
    public static void ApplyCancelled(PlacementBid bid, DateTime now)
    {
        if (bid.Status is PlacementBidStatus.Cancelled or PlacementBidStatus.Outbid)
        {
            return;
        }

        bid.UpdatedAt = now;
        if (bid.PaymentStatus == PaymentState.Paid)
        {
            Refund(bid, PlacementBidStatus.Cancelled, "Cancelled");
        }
        else
        {
            bid.Status = PlacementBidStatus.Cancelled;
            bid.PaymentStatus = PaymentState.Failed;
            bid.FailureReason = "Cancelled";
        }
    }
}

/// <summary>
/// Paid placement: stores bid for first place in a pincode for a week, pay up front, and the highest paid bid leads. Store staff bid for
/// their own store; an administrator can bid for any store and manages the rules. A beaten bid is refunded in full.
/// </summary>
public sealed class PlacementService(
    IPlacementStore store,
    IPaymentGateway gateway,
    PaymentSettings settings,
    IAuthorizationScopeService scopeService,
    ICurrentUserContextResolver userContext,
    TimeProvider time,
    ILogger<PlacementService> log) : IPlacementService
{
    private const int MaxOpenBids = 20;
    private const long MaxAmountPaise = 1_000_000_000; // ₹1,00,00,000, so a typo cannot ask for a fortune
    private const string NotAllowed = "Only an administrator or a store can manage placement";
    private const string Unavailable = "Online payment is not available right now.";

    private sealed record Actor(bool Admin, Guid OrganizationId, Guid? StoreId, string By);

    private async Task<Actor?> ResolveAsync(CancellationToken cancellationToken)
    {
        var scope = await scopeService.ResolveAsync(cancellationToken);
        if (scope?.IsApplicationAdmin == true)
        {
            return new Actor(true, scope.OrganizationId, null, scope.UserId.ToString());
        }

        var context = await userContext.ResolveAsync(cancellationToken);
        return context is { Role: Role.StoreStaff, StoreId: { } storeId } ? new Actor(false, context.OrganizationId, storeId, context.UserId.ToString()) : null;
    }

    private static PlacementRulesResponse Map(PlacementRules rules) => new(rules.Enabled, rules.MinBidPaise, rules.MinStepPaise, rules.MaxWeeksAhead);

    private static PlacementBidResponse Map(PlacementBid bid) => new(
        bid.Id,
        bid.StoreId,
        bid.Store?.Name ?? string.Empty,
        bid.Pincode,
        bid.WeekStart,
        PlacementWeeks.StartUtc(bid.WeekStart),
        bid.AmountPaise,
        bid.Status.ToString(),
        bid.PaymentStatus,
        DateTime.SpecifyKind(bid.CreatedAt, DateTimeKind.Utc));

    public async Task<CampaignOperationResult<PlacementRulesResponse>> GetRulesAsync(CancellationToken cancellationToken = default) =>
        await ResolveAsync(cancellationToken) is null
            ? new(CampaignOperationStatus.Unauthorized, Message: NotAllowed)
            : new(CampaignOperationStatus.Succeeded, Map(await store.GetRulesAsync(cancellationToken)));

    public async Task<CampaignOperationResult<PlacementRulesResponse>> SetRulesAsync(SetPlacementRulesRequest request, CancellationToken cancellationToken = default)
    {
        var actor = await ResolveAsync(cancellationToken);
        if (actor is not { Admin: true })
        {
            return new(CampaignOperationStatus.Unauthorized, Message: "Only an administrator can change the placement rules");
        }

        if (request.MinBidPaise <= 0 || request.MinBidPaise > MaxAmountPaise || request.MinStepPaise < 0 || request.MinStepPaise > MaxAmountPaise || request.MaxWeeksAhead is < 1 or > 26)
        {
            return new(CampaignOperationStatus.InvalidRequest, Message: "The minimum bid must be above 0, the step 0 or more, and the weeks ahead between 1 and 26");
        }

        var rules = await store.GetRulesAsync(cancellationToken);
        rules.Enabled = request.Enabled;
        rules.MinBidPaise = request.MinBidPaise;
        rules.MinStepPaise = request.MinStepPaise;
        rules.MaxWeeksAhead = request.MaxWeeksAhead;
        rules.UpdatedAt = time.GetUtcNow().UtcDateTime;
        rules.UpdatedBy = actor.By;
        await store.SetRulesAsync(rules, cancellationToken);
        return new(CampaignOperationStatus.Succeeded, Map(rules));
    }

    public async Task<CampaignOperationResult<PlacementMarketResponse>> GetMarketAsync(string pincode, CancellationToken cancellationToken = default)
    {
        var actor = await ResolveAsync(cancellationToken);
        if (actor is null)
        {
            return new(CampaignOperationStatus.Unauthorized, Message: NotAllowed);
        }

        if (!PlacementWeeks.IsPincode(pincode))
        {
            return new(CampaignOperationStatus.InvalidRequest, Message: "Give a six-digit pincode");
        }

        var rules = await store.GetRulesAsync(cancellationToken);
        if (!rules.Enabled)
        {
            return new(CampaignOperationStatus.Conflict, Message: "Paid placement is switched off", Reason: PlacementReasons.Disabled);
        }

        var current = PlacementWeeks.WeekStartOf(time.GetUtcNow().UtcDateTime);
        var weeks = new List<PlacementWeekInfo>();
        for (var ahead = 1; ahead <= rules.MaxWeeksAhead; ahead++)
        {
            var week = current.AddDays(7 * ahead);
            var leading = await store.GetLeadingAsync(pincode, week, cancellationToken);
            weeks.Add(new PlacementWeekInfo(week, PlacementWeeks.StartUtc(week), leading?.AmountPaise, leading is not null && leading.StoreId == actor.StoreId, MinNext(rules, leading)));
        }

        return new(CampaignOperationStatus.Succeeded, new PlacementMarketResponse(pincode, weeks));
    }

    private static long MinNext(PlacementRules rules, PlacementBid? leading) => leading is null ? rules.MinBidPaise : Math.Max(rules.MinBidPaise, leading.AmountPaise + Math.Max(rules.MinStepPaise, 1));

    public async Task<CampaignOperationResult<PlacementBidResponse>> PlaceBidAsync(PlaceBidRequest request, CancellationToken cancellationToken = default)
    {
        var actor = await ResolveAsync(cancellationToken);
        if (actor is null)
        {
            return new(CampaignOperationStatus.Unauthorized, Message: NotAllowed);
        }

        var rules = await store.GetRulesAsync(cancellationToken);
        if (!rules.Enabled)
        {
            return new(CampaignOperationStatus.Conflict, Message: "Paid placement is switched off", Reason: PlacementReasons.Disabled);
        }

        if (!PlacementWeeks.IsPincode(request.Pincode))
        {
            return new(CampaignOperationStatus.InvalidRequest, Message: "Give a six-digit pincode");
        }

        if (!PlacementWeeks.IsMonday(request.WeekStart) || request.AmountPaise <= 0 || request.AmountPaise > MaxAmountPaise)
        {
            return new(CampaignOperationStatus.InvalidRequest, Message: "A bid is for a week starting on a Monday, with an amount above 0");
        }

        var now = time.GetUtcNow().UtcDateTime;
        var current = PlacementWeeks.WeekStartOf(now);
        if (now >= PlacementWeeks.StartUtc(request.WeekStart) || request.WeekStart > current.AddDays(7 * rules.MaxWeeksAhead))
        {
            return new(CampaignOperationStatus.Conflict, Message: "Bidding is closed for that week or not open yet", Reason: PlacementReasons.Closed);
        }

        var storeId = actor.Admin ? request.StoreId : actor.StoreId;
        if (storeId is null)
        {
            return new(CampaignOperationStatus.InvalidRequest, Message: "Say which store the bid is for");
        }

        if (!actor.Admin && request.StoreId is { } other && other != actor.StoreId)
        {
            return new(CampaignOperationStatus.Unauthorized, Message: "You can only bid for your own store");
        }

        var target = await store.GetStoreAsync(storeId.Value, actor.OrganizationId, cancellationToken);
        if (target is null)
        {
            return new(CampaignOperationStatus.InvalidRequest, Message: "That store is not known");
        }

        var leading = await store.GetLeadingAsync(request.Pincode, request.WeekStart, cancellationToken);
        var minimum = MinNext(rules, leading);
        if (request.AmountPaise < minimum)
        {
            return new(CampaignOperationStatus.Conflict, Message: $"The bid must be at least ₹{minimum / 100m:0.##}", Reason: PlacementReasons.TooLow);
        }

        if (await store.CountOpenBidsAsync(storeId.Value, cancellationToken) >= MaxOpenBids)
        {
            return new(CampaignOperationStatus.Conflict, Message: "Too many bids are waiting for payment; pay or cancel some first", Reason: PlacementReasons.TooManyOpen);
        }

        var bid = new PlacementBid { StoreId = storeId.Value, Pincode = request.Pincode, WeekStart = request.WeekStart, AmountPaise = request.AmountPaise, CreatedAt = now, UpdatedAt = now };
        await store.AddBidAsync(bid, cancellationToken);
        bid.Store = target;
        return new(CampaignOperationStatus.Succeeded, Map(bid));
    }

    public async Task<CampaignOperationResult<IReadOnlyList<PlacementBidResponse>>> ListBidsAsync(CancellationToken cancellationToken = default)
    {
        var actor = await ResolveAsync(cancellationToken);
        if (actor is null)
        {
            return new(CampaignOperationStatus.Unauthorized, Message: NotAllowed);
        }

        var bids = await store.ListBidsAsync(actor.Admin ? null : actor.StoreId, actor.Admin ? 200 : 100, cancellationToken);
        return new(CampaignOperationStatus.Succeeded, bids.Select(Map).ToArray());
    }

    /// <summary>The bid, if the caller may act on it: any bid for an administrator; for store staff only their own store's.</summary>
    private async Task<PlacementBid?> FindAsync(Actor actor, Guid bidId, CancellationToken cancellationToken)
    {
        var bid = await store.GetBidAsync(bidId, cancellationToken);
        return bid is not null && (actor.Admin || bid.StoreId == actor.StoreId) ? bid : null;
    }

    public async Task<CampaignOperationResult<PlacementBidResponse>> CancelBidAsync(Guid bidId, CancellationToken cancellationToken = default)
    {
        var actor = await ResolveAsync(cancellationToken);
        if (actor is null)
        {
            return new(CampaignOperationStatus.Unauthorized, Message: NotAllowed);
        }

        var bid = await FindAsync(actor, bidId, cancellationToken);
        if (bid is null)
        {
            return new(CampaignOperationStatus.NotFound, Message: "Bid not found");
        }

        // A store cannot take back money it has already committed; only an administrator can cancel a paid bid.
        if (!actor.Admin && bid.PaymentStatus == PaymentState.Paid)
        {
            return new(CampaignOperationStatus.Conflict, Map(bid), "A paid bid cannot be withdrawn. Ask an administrator.", PlacementReasons.NotAwaitingPayment);
        }

        var cancelled = await store.CancelBidAsync(bidId, time.GetUtcNow().UtcDateTime, cancellationToken) ?? bid;
        return new(CampaignOperationStatus.Succeeded, Map(cancelled));
    }

    public async Task<PaymentOperationResult<StartPaymentResponse>> StartPaymentAsync(Guid bidId, CancellationToken cancellationToken = default)
    {
        if (!settings.Enabled)
        {
            return PaymentOperationResult<StartPaymentResponse>.Conflict(Unavailable, PaymentReasons.PaymentsUnavailable);
        }

        var actor = await ResolveAsync(cancellationToken);
        var bid = actor is null ? null : await FindAsync(actor, bidId, cancellationToken);
        if (bid is null)
        {
            return PaymentOperationResult<StartPaymentResponse>.NotFound("Bid not found");
        }

        if (bid.PaymentStatus == PaymentState.Paid)
        {
            return PaymentOperationResult<StartPaymentResponse>.Conflict("This bid is already paid.", PaymentReasons.AlreadyPaid);
        }

        var now = time.GetUtcNow().UtcDateTime;
        if (now >= PlacementWeeks.StartUtc(bid.WeekStart))
        {
            return PaymentOperationResult<StartPaymentResponse>.Conflict("Bidding for that week has closed.", PlacementReasons.Closed);
        }

        if (!(await store.GetRulesAsync(cancellationToken)).Enabled)
        {
            return PaymentOperationResult<StartPaymentResponse>.Conflict("Paid placement is switched off.", PlacementReasons.Disabled);
        }

        var attempt = await store.BeginAttemptAsync(bidId, now, cancellationToken);
        if (attempt is null)
        {
            return PaymentOperationResult<StartPaymentResponse>.Conflict("This bid is not waiting for payment.", PlacementReasons.NotAwaitingPayment);
        }

        if (attempt.ProviderOrderId is null)
        {
            try
            {
                var created = await gateway.CreateOrderAsync(attempt.AmountPaise, attempt.Currency, "bid-" + bidId.ToString("N")[..16], cancellationToken);
                await store.AttachProviderOrderAsync(bidId, created.Id, cancellationToken);
            }
            catch (PaymentGatewayException ex)
            {
                log.LogWarning("Could not create the provider order for a placement bid: {Code}", ex.Code);
                return PaymentOperationResult<StartPaymentResponse>.Conflict(Unavailable, PaymentReasons.ProviderUnavailable);
            }

            attempt = await store.GetBidAsync(bidId, cancellationToken) ?? attempt;
        }

        var closes = PlacementWeeks.StartUtc(bid.WeekStart);
        return PaymentOperationResult<StartPaymentResponse>.Succeeded(new StartPaymentResponse(attempt.Provider, settings.KeyId, attempt.ProviderOrderId!, attempt.AmountPaise, attempt.Currency, closes, "Placement " + bid.Pincode));
    }

    public async Task<PaymentOperationResult<PlacementBidResponse>> ConfirmPaymentAsync(Guid bidId, ConfirmPaymentRequest request, CancellationToken cancellationToken = default)
    {
        if (!settings.Enabled)
        {
            return PaymentOperationResult<PlacementBidResponse>.Conflict(Unavailable, PaymentReasons.PaymentsUnavailable);
        }

        if (string.IsNullOrWhiteSpace(request.ProviderOrderId) || string.IsNullOrWhiteSpace(request.ProviderPaymentId) || string.IsNullOrWhiteSpace(request.Signature)
            || request.ProviderOrderId.Length > 100 || request.ProviderPaymentId.Length > 100 || request.Signature.Length > 200)
        {
            return PaymentOperationResult<PlacementBidResponse>.Invalid("The payment details are not complete");
        }

        var actor = await ResolveAsync(cancellationToken);
        var bid = actor is null ? null : await FindAsync(actor, bidId, cancellationToken);
        if (bid is null)
        {
            return PaymentOperationResult<PlacementBidResponse>.NotFound("Bid not found");
        }

        if (bid.ProviderOrderId is null || bid.ProviderOrderId != request.ProviderOrderId)
        {
            return PaymentOperationResult<PlacementBidResponse>.Conflict("This payment is not for this bid.", PaymentReasons.Mismatch);
        }

        if (bid.PaymentStatus == PaymentState.Paid)
        {
            return PaymentOperationResult<PlacementBidResponse>.Succeeded(Map(bid));
        }

        if (!gateway.VerifyPaymentSignature(request.ProviderOrderId, request.ProviderPaymentId, request.Signature))
        {
            log.LogWarning("A placement payment result with a bad signature was sent for bid {BidId}", bidId);
            return PaymentOperationResult<PlacementBidResponse>.Conflict("The payment could not be verified.", PaymentReasons.SignatureInvalid);
        }

        GatewayPayment? paid;
        try
        {
            paid = await gateway.FetchPaymentAsync(request.ProviderPaymentId, cancellationToken);
        }
        catch (PaymentGatewayException ex)
        {
            log.LogWarning("Could not read a placement payment from the provider: {Code}", ex.Code);
            return PaymentOperationResult<PlacementBidResponse>.Conflict("We could not confirm your payment yet. It will be confirmed shortly.", PaymentReasons.ProviderUnavailable);
        }

        if (paid is null || paid.OrderId != bid.ProviderOrderId)
        {
            return PaymentOperationResult<PlacementBidResponse>.Conflict("This payment is not for this bid.", PaymentReasons.Mismatch);
        }

        if (!string.Equals(paid.Status, "captured", StringComparison.OrdinalIgnoreCase))
        {
            return PaymentOperationResult<PlacementBidResponse>.Conflict("The payment has not been completed yet.", PaymentReasons.NotCaptured);
        }

        var outcome = await store.ApplyCapturedAsync(bid.ProviderOrderId, request.ProviderPaymentId, paid.AmountPaise, time.GetUtcNow().UtcDateTime, cancellationToken);
        var fresh = await store.GetBidAsync(bidId, cancellationToken) ?? bid;
        return outcome switch
        {
            PlacementCaptureOutcome.Leading or PlacementCaptureOutcome.AlreadyPaid => PaymentOperationResult<PlacementBidResponse>.Succeeded(Map(fresh)),
            PlacementCaptureOutcome.Outbid => PaymentOperationResult<PlacementBidResponse>.Conflict("A higher bid was paid first, so your payment will be refunded in full.", PlacementReasons.Outbid),
            PlacementCaptureOutcome.AmountMismatch => PaymentOperationResult<PlacementBidResponse>.Conflict("The amount paid did not match the bid, so it will be refunded.", PaymentReasons.AmountMismatch),
            _ => PaymentOperationResult<PlacementBidResponse>.Conflict("Bidding for that week has closed, so your payment will be refunded in full.", PlacementReasons.Closed)
        };
    }
}

public sealed class PlacementMaintenance(IPlacementStore store, IPaymentGateway gateway, PaymentSettings settings, TimeProvider time, ILogger<PlacementMaintenance> log) : IPlacementMaintenance
{
    private const int BatchSize = 50;

    public async Task<int> StartRefundsAsync(CancellationToken cancellationToken = default)
    {
        var started = 0;
        foreach (var bid in await store.GetRefundsToStartAsync(BatchSize, cancellationToken))
        {
            var now = time.GetUtcNow().UtcDateTime;
            if (bid.ProviderPaymentId is null)
            {
                await store.SetRefundFailedAsync(bid.Id, "No provider payment to refund", permanent: true, now, cancellationToken);
                continue;
            }

            try
            {
                var refund = await gateway.RefundAsync(bid.ProviderPaymentId, bid.AmountPaise, "bid-refund-" + bid.Id.ToString("N"), cancellationToken);
                await store.SetRefundStartedAsync(bid.Id, refund.Id, string.Equals(refund.Status, "processed", StringComparison.OrdinalIgnoreCase), now, cancellationToken);
                started++;
            }
            catch (PaymentGatewayException ex)
            {
                log.LogWarning("Refund of a placement bid {BidId} was not made ({Code}, transient: {Transient})", bid.Id, ex.Code, ex.Transient);
                await store.SetRefundFailedAsync(bid.Id, ex.Message, permanent: !ex.Transient, now, cancellationToken);
            }
        }

        return started;
    }

    public async Task<int> ReconcileAsync(CancellationToken cancellationToken = default)
    {
        var now = time.GetUtcNow().UtcDateTime;
        var changed = 0;
        foreach (var bid in await store.GetUnsettledAsync(now.AddMinutes(-settings.ReconcileAfterMinutes), BatchSize, cancellationToken))
        {
            try
            {
                if (bid.PaymentStatus == PaymentState.Refunding)
                {
                    var paid = bid.ProviderPaymentId is null ? null : await gateway.FetchPaymentAsync(bid.ProviderPaymentId, cancellationToken);
                    if (paid is not null && paid.AmountRefundedPaise >= bid.AmountPaise)
                    {
                        await store.SetRefundedAsync(bid.Id, now, cancellationToken);
                        changed++;
                    }

                    continue;
                }

                var attempts = await gateway.FetchOrderPaymentsAsync(bid.ProviderOrderId!, cancellationToken);
                var captured = attempts.FirstOrDefault(item => string.Equals(item.Status, "captured", StringComparison.OrdinalIgnoreCase));
                if (captured is not null)
                {
                    await store.ApplyCapturedAsync(bid.ProviderOrderId!, captured.Id, captured.AmountPaise, now, cancellationToken);
                    changed++;
                }
            }
            catch (PaymentGatewayException ex)
            {
                log.LogWarning("Could not look up a placement payment {BidId} ({Code})", bid.Id, ex.Code);
            }
        }

        return changed;
    }
}
