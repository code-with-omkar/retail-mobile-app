using Microsoft.Extensions.Logging;
using QuickCommerce.Application.Interfaces;
using QuickCommerce.Domain;

namespace QuickCommerce.Application.Services;

/// <summary>How a campaign looks to the person managing it.</summary>
public static class CampaignMapping
{
    public static CampaignResponse ToResponse(Campaign campaign) => new(
        campaign.Id,
        campaign.TitleEn,
        campaign.BodyEn,
        campaign.TitleMr,
        campaign.BodyMr,
        DateTime.SpecifyKind(campaign.StartsAt, DateTimeKind.Utc),
        StatusOf(campaign),
        campaign.RecipientCount,
        DateTime.SpecifyKind(campaign.CreatedAt, DateTimeKind.Utc),
        campaign.Stores.Select(link => new CampaignStoreInfo(link.StoreId, link.Store?.Name ?? string.Empty)).OrderBy(info => info.Name).ToArray(),
        campaign.Stores.Count == 0,
        campaign.FeePaise,
        campaign.PaymentStatus);

    public static string StatusOf(Campaign campaign) =>
        !campaign.IsActive ? CampaignStatuses.Cancelled
        : campaign.PublishedAt is not null ? CampaignStatuses.Sent
        : campaign.FeePaise > 0 && campaign.PaymentStatus != PaymentState.Paid ? CampaignStatuses.AwaitingPayment
        : CampaignStatuses.Scheduled;

    /// <summary>Whether the offer is waiting for its fee: active, not sent, and not paid.</summary>
    public static bool AwaitsPayment(Campaign campaign) => StatusOf(campaign) == CampaignStatuses.AwaitingPayment;
}

/// <summary>
/// What happens to an offer and its fee payment at each payment event. Pure rules on the entities, so the SQL store and the in-memory
/// store cannot disagree about them.
/// </summary>
public static class CampaignPaymentTransitions
{
    public static CapturedOutcome ApplyCaptured(Campaign campaign, CampaignPayment payment, string providerPaymentId, long paidPaise, DateTime now)
    {
        if (payment.Status is PaymentState.Paid or PaymentState.Refunding or PaymentState.Refunded or PaymentState.RefundFailed)
        {
            return CapturedOutcome.AlreadyPaid;
        }

        payment.ProviderPaymentId = providerPaymentId;
        payment.UpdatedAt = now;
        if (paidPaise != payment.AmountPaise)
        {
            payment.Status = PaymentState.Refunding;
            payment.FailureReason = "AmountMismatch";
            return CapturedOutcome.AmountMismatch;
        }

        // Paid after the offer was cancelled or sent: the fee goes back in full and the offer is left as it is.
        if (!campaign.IsActive || campaign.PublishedAt is not null)
        {
            payment.Status = PaymentState.Refunding;
            payment.FailureReason = "Late";
            campaign.PaymentStatus = PaymentState.Refunding;
            return CapturedOutcome.LateRefund;
        }

        payment.Status = PaymentState.Paid;
        payment.FailureReason = null;
        campaign.PaymentStatus = PaymentState.Paid;
        return CapturedOutcome.Paid;
    }

    /// <summary>An offer was cancelled before it was sent: a paid fee is queued for a full refund, an unpaid one is closed.</summary>
    public static void ApplyCancelled(Campaign campaign, CampaignPayment? payment, DateTime now)
    {
        if (payment is { Status: PaymentState.Paid })
        {
            payment.Status = PaymentState.Refunding;
            payment.FailureReason = "Cancelled";
            payment.UpdatedAt = now;
            campaign.PaymentStatus = PaymentState.Refunding;
        }
        else if (payment is { Status: PaymentState.Created or PaymentState.Failed })
        {
            payment.Status = PaymentState.Failed;
            payment.FailureReason = "Cancelled";
            payment.UpdatedAt = now;
            campaign.PaymentStatus = PaymentState.Failed;
        }
        else if (campaign.FeePaise > 0 && campaign.PaymentStatus == PaymentState.Created)
        {
            campaign.PaymentStatus = PaymentState.Failed;
        }
    }

    public static void ApplyRefunded(Campaign campaign, CampaignPayment payment, DateTime now)
    {
        payment.Status = PaymentState.Refunded;
        payment.UpdatedAt = now;
        campaign.PaymentStatus = PaymentState.Refunded;
    }

    public static void ApplyRefundFailed(Campaign campaign, CampaignPayment payment, string reason, DateTime now)
    {
        payment.Status = PaymentState.RefundFailed;
        payment.FailureReason = reason.Length <= 200 ? reason : reason[..200];
        payment.UpdatedAt = now;
        campaign.PaymentStatus = PaymentState.RefundFailed;
    }
}

public sealed class CampaignPaymentService(
    ICampaignStore campaigns,
    ICampaignPaymentStore payments,
    IPaymentGateway gateway,
    PaymentSettings settings,
    IAuthorizationScopeService scopeService,
    ICurrentUserContextResolver userContext,
    TimeProvider time,
    ILogger<CampaignPaymentService> log) : ICampaignPaymentService
{
    private const string Unavailable = "Online payment is not available right now.";

    public async Task<PaymentOperationResult<StartPaymentResponse>> StartAsync(Guid campaignId, CancellationToken cancellationToken = default)
    {
        if (!settings.Enabled)
        {
            return PaymentOperationResult<StartPaymentResponse>.Conflict(Unavailable, PaymentReasons.PaymentsUnavailable);
        }

        var campaign = await FindAsync(campaignId, cancellationToken);
        if (campaign is null)
        {
            return PaymentOperationResult<StartPaymentResponse>.NotFound("Offer not found");
        }

        if (campaign.PaymentStatus == PaymentState.Paid)
        {
            return PaymentOperationResult<StartPaymentResponse>.Conflict("This offer is already paid for.", PaymentReasons.AlreadyPaid);
        }

        if (!CampaignMapping.AwaitsPayment(campaign))
        {
            return PaymentOperationResult<StartPaymentResponse>.Conflict("This offer is not waiting for payment.", CampaignReasons.NotAwaitingPayment);
        }

        var now = time.GetUtcNow().UtcDateTime;
        var payment = await payments.BeginAttemptAsync(campaignId, now, cancellationToken);
        if (payment is null)
        {
            return PaymentOperationResult<StartPaymentResponse>.Conflict("This offer is not waiting for payment.", CampaignReasons.NotAwaitingPayment);
        }

        if (payment.ProviderOrderId is null)
        {
            try
            {
                var created = await gateway.CreateOrderAsync(payment.AmountPaise, payment.Currency, "offer-" + campaignId.ToString("N")[..16], cancellationToken);
                await payments.AttachProviderOrderAsync(payment.Id, created.Id, cancellationToken);
            }
            catch (PaymentGatewayException ex)
            {
                log.LogWarning("Could not create the provider order for an offer fee: {Code}", ex.Code);
                return PaymentOperationResult<StartPaymentResponse>.Conflict(Unavailable, PaymentReasons.ProviderUnavailable);
            }

            // Whoever attached first won: read what is stored so every request uses the same provider order.
            payment = await payments.GetPaymentAsync(campaignId, cancellationToken) ?? payment;
        }

        var label = "Offer: " + (campaign.TitleEn.Length <= 30 ? campaign.TitleEn : campaign.TitleEn[..30]);
        return PaymentOperationResult<StartPaymentResponse>.Succeeded(new StartPaymentResponse(payment.Provider, settings.KeyId, payment.ProviderOrderId!, payment.AmountPaise, payment.Currency, now.AddHours(1), label));
    }

    public async Task<PaymentOperationResult<CampaignResponse>> ConfirmAsync(Guid campaignId, ConfirmPaymentRequest request, CancellationToken cancellationToken = default)
    {
        if (!settings.Enabled)
        {
            return PaymentOperationResult<CampaignResponse>.Conflict(Unavailable, PaymentReasons.PaymentsUnavailable);
        }

        if (string.IsNullOrWhiteSpace(request.ProviderOrderId) || string.IsNullOrWhiteSpace(request.ProviderPaymentId) || string.IsNullOrWhiteSpace(request.Signature)
            || request.ProviderOrderId.Length > 100 || request.ProviderPaymentId.Length > 100 || request.Signature.Length > 200)
        {
            return PaymentOperationResult<CampaignResponse>.Invalid("The payment details are not complete");
        }

        var campaign = await FindAsync(campaignId, cancellationToken);
        if (campaign is null)
        {
            return PaymentOperationResult<CampaignResponse>.NotFound("Offer not found");
        }

        var payment = await payments.GetPaymentAsync(campaignId, cancellationToken);
        if (payment?.ProviderOrderId is null || payment.ProviderOrderId != request.ProviderOrderId)
        {
            return PaymentOperationResult<CampaignResponse>.Conflict("This payment is not for this offer.", PaymentReasons.Mismatch);
        }

        if (payment.Status == PaymentState.Paid)
        {
            return PaymentOperationResult<CampaignResponse>.Succeeded(CampaignMapping.ToResponse(campaign));
        }

        if (!gateway.VerifyPaymentSignature(request.ProviderOrderId, request.ProviderPaymentId, request.Signature))
        {
            log.LogWarning("An offer payment result with a bad signature was sent for campaign {CampaignId}", campaignId);
            return PaymentOperationResult<CampaignResponse>.Conflict("The payment could not be verified.", PaymentReasons.SignatureInvalid);
        }

        GatewayPayment? paid;
        try
        {
            paid = await gateway.FetchPaymentAsync(request.ProviderPaymentId, cancellationToken);
        }
        catch (PaymentGatewayException ex)
        {
            log.LogWarning("Could not read an offer payment from the provider: {Code}", ex.Code);
            return PaymentOperationResult<CampaignResponse>.Conflict("We could not confirm your payment yet. It will be confirmed shortly.", PaymentReasons.ProviderUnavailable);
        }

        if (paid is null || paid.OrderId != payment.ProviderOrderId)
        {
            return PaymentOperationResult<CampaignResponse>.Conflict("This payment is not for this offer.", PaymentReasons.Mismatch);
        }

        if (!string.Equals(paid.Status, "captured", StringComparison.OrdinalIgnoreCase))
        {
            return PaymentOperationResult<CampaignResponse>.Conflict("The payment has not been completed yet.", PaymentReasons.NotCaptured);
        }

        var outcome = await payments.ApplyCapturedAsync(payment.ProviderOrderId, request.ProviderPaymentId, paid.AmountPaise, time.GetUtcNow().UtcDateTime, cancellationToken);
        var fresh = await campaigns.GetCampaignAsync(campaignId, cancellationToken) ?? campaign;
        return outcome switch
        {
            CapturedOutcome.Paid or CapturedOutcome.AlreadyPaid => PaymentOperationResult<CampaignResponse>.Succeeded(CampaignMapping.ToResponse(fresh)),
            CapturedOutcome.AmountMismatch => PaymentOperationResult<CampaignResponse>.Conflict("The amount paid did not match the fee, so it will be refunded.", PaymentReasons.AmountMismatch),
            _ => PaymentOperationResult<CampaignResponse>.Conflict("This offer can no longer be paid for, so the payment will be refunded.", CampaignReasons.NotAwaitingPayment)
        };
    }

    /// <summary>The offer, if the caller may pay for it: any offer for an administrator; for store staff only an offer for their store alone.</summary>
    private async Task<Campaign?> FindAsync(Guid campaignId, CancellationToken cancellationToken)
    {
        var campaign = await campaigns.GetCampaignAsync(campaignId, cancellationToken);
        if (campaign is null)
        {
            return null;
        }

        if ((await scopeService.ResolveAsync(cancellationToken))?.IsApplicationAdmin == true)
        {
            return campaign;
        }

        var context = await userContext.ResolveAsync(cancellationToken);
        return context is { Role: Role.StoreStaff, StoreId: { } storeId } && campaign.Stores.Count == 1 && campaign.Stores[0].StoreId == storeId ? campaign : null;
    }
}

public sealed class CampaignPaymentMaintenance(ICampaignPaymentStore store, IPaymentGateway gateway, PaymentSettings settings, TimeProvider time, ILogger<CampaignPaymentMaintenance> log) : ICampaignPaymentMaintenance
{
    private const int BatchSize = 50;

    public async Task<int> StartRefundsAsync(CancellationToken cancellationToken = default)
    {
        var started = 0;
        foreach (var payment in await store.GetRefundsToStartAsync(BatchSize, cancellationToken))
        {
            var now = time.GetUtcNow().UtcDateTime;
            if (payment.ProviderPaymentId is null)
            {
                await store.SetRefundFailedAsync(payment.Id, "No provider payment to refund", permanent: true, now, cancellationToken);
                continue;
            }

            try
            {
                var refund = await gateway.RefundAsync(payment.ProviderPaymentId, payment.AmountPaise, "offer-refund-" + payment.Id.ToString("N"), cancellationToken);
                await store.SetRefundStartedAsync(payment.Id, refund.Id, string.Equals(refund.Status, "processed", StringComparison.OrdinalIgnoreCase), now, cancellationToken);
                started++;
            }
            catch (PaymentGatewayException ex)
            {
                log.LogWarning("Refund of an offer fee {PaymentId} was not made ({Code}, transient: {Transient})", payment.Id, ex.Code, ex.Transient);
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
                if (payment.Status == PaymentState.Refunding)
                {
                    var paid = payment.ProviderPaymentId is null ? null : await gateway.FetchPaymentAsync(payment.ProviderPaymentId, cancellationToken);
                    if (paid is not null && paid.AmountRefundedPaise >= payment.AmountPaise)
                    {
                        await store.SetRefundedAsync(payment.Id, now, cancellationToken);
                        changed++;
                    }

                    continue;
                }

                var attempts = await gateway.FetchOrderPaymentsAsync(payment.ProviderOrderId!, cancellationToken);
                var captured = attempts.FirstOrDefault(item => string.Equals(item.Status, "captured", StringComparison.OrdinalIgnoreCase));
                if (captured is not null)
                {
                    await store.ApplyCapturedAsync(payment.ProviderOrderId!, captured.Id, captured.AmountPaise, now, cancellationToken);
                    changed++;
                }
            }
            catch (PaymentGatewayException ex)
            {
                log.LogWarning("Could not look up an offer fee payment {PaymentId} ({Code})", payment.Id, ex.Code);
            }
        }

        return changed;
    }
}

public sealed class OfferFeeService(
    IOfferFeeStore fees,
    IAuthorizationScopeService scopeService,
    ICurrentUserContextResolver userContext,
    PaymentSettings? payments = null,
    TimeProvider? timeProvider = null) : IOfferFeeService
{
    /// <summary>A fee is at most ₹1,00,000, so a slip of the finger cannot ask a store for a fortune.</summary>
    public const int MaxPaise = 10_000_000;

    private readonly TimeProvider clock = timeProvider ?? TimeProvider.System;

    public async Task<CampaignOperationResult<OfferFeesResponse>> GetAsync(CancellationToken cancellationToken = default)
    {
        var scope = await scopeService.ResolveAsync(cancellationToken);
        return scope?.IsApplicationAdmin == true
            ? new(CampaignOperationStatus.Succeeded, await BuildAsync(scope.OrganizationId, cancellationToken))
            : new(CampaignOperationStatus.Unauthorized, Message: "Only an administrator can see or change offer fees");
    }

    public async Task<CampaignOperationResult<OfferFeesResponse>> SetDefaultAsync(int amountPaise, CancellationToken cancellationToken = default)
    {
        var scope = await scopeService.ResolveAsync(cancellationToken);
        if (scope?.IsApplicationAdmin != true)
        {
            return new(CampaignOperationStatus.Unauthorized, Message: "Only an administrator can see or change offer fees");
        }

        if (amountPaise < 0 || amountPaise > MaxPaise)
        {
            return new(CampaignOperationStatus.InvalidRequest, Message: "The fee must be between 0 and ₹1,00,000");
        }

        await fees.SetAsync(null, amountPaise, scope.UserId.ToString(), clock.GetUtcNow().UtcDateTime, cancellationToken);
        return new(CampaignOperationStatus.Succeeded, await BuildAsync(scope.OrganizationId, cancellationToken));
    }

    public async Task<CampaignOperationResult<OfferFeesResponse>> SetStoreAsync(Guid storeId, int? amountPaise, CancellationToken cancellationToken = default)
    {
        var scope = await scopeService.ResolveAsync(cancellationToken);
        if (scope?.IsApplicationAdmin != true)
        {
            return new(CampaignOperationStatus.Unauthorized, Message: "Only an administrator can see or change offer fees");
        }

        if (amountPaise is < 0 or > MaxPaise)
        {
            return new(CampaignOperationStatus.InvalidRequest, Message: "The fee must be between 0 and ₹1,00,000");
        }

        if ((await fees.GetStoresAsync(scope.OrganizationId, cancellationToken)).All(store => store.Id != storeId))
        {
            return new(CampaignOperationStatus.NotFound, Message: "Store not found");
        }

        await fees.SetAsync(storeId, amountPaise, scope.UserId.ToString(), clock.GetUtcNow().UtcDateTime, cancellationToken);
        return new(CampaignOperationStatus.Succeeded, await BuildAsync(scope.OrganizationId, cancellationToken));
    }

    public async Task<CampaignOperationResult<MyOfferFee>> GetMineAsync(CancellationToken cancellationToken = default)
    {
        var scope = await scopeService.ResolveAsync(cancellationToken);
        var available = payments?.Enabled == true;
        if (scope?.IsApplicationAdmin == true)
        {
            // An administrator announces for the business, free.
            return new(CampaignOperationStatus.Succeeded, new MyOfferFee(0, available));
        }

        var context = await userContext.ResolveAsync(cancellationToken);
        if (context is not { Role: Role.StoreStaff, StoreId: { } storeId })
        {
            return new(CampaignOperationStatus.Unauthorized, Message: "Only an administrator or a store can manage offers");
        }

        return new(CampaignOperationStatus.Succeeded, new MyOfferFee(await EffectiveAsync(fees, storeId, cancellationToken), available));
    }

    /// <summary>The fee for a store's offer: its own setting, else the default.</summary>
    public static async Task<int> EffectiveAsync(IOfferFeeStore fees, Guid storeId, CancellationToken cancellationToken = default) =>
        (await fees.GetOverridesAsync(cancellationToken)).TryGetValue(storeId, out var own) ? own : await fees.GetDefaultAsync(cancellationToken);

    private async Task<OfferFeesResponse> BuildAsync(Guid organizationId, CancellationToken cancellationToken)
    {
        var defaultFee = await fees.GetDefaultAsync(cancellationToken);
        var overrides = await fees.GetOverridesAsync(cancellationToken);
        var stores = (await fees.GetStoresAsync(organizationId, cancellationToken))
            .OrderBy(store => store.Name)
            .Select(store => new StoreOfferFee(store.Id, store.Name, overrides.TryGetValue(store.Id, out var own) ? own : null, overrides.TryGetValue(store.Id, out var effective) ? effective : defaultFee))
            .ToArray();
        return new OfferFeesResponse(defaultFee, stores);
    }
}
