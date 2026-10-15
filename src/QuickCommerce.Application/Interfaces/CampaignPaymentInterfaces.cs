using QuickCommerce.Domain;

namespace QuickCommerce.Application.Interfaces;

/// <summary>What one store pays to publish an offer: its own setting if it has one, otherwise the default. 0 is free.</summary>
public sealed record StoreOfferFee(Guid StoreId, string Name, int? OverridePaise, int EffectivePaise);

public sealed record OfferFeesResponse(int DefaultPaise, IReadOnlyList<StoreOfferFee> Stores, int LoadPaise = 0);

/// <summary>A fee in paise; for a store, null removes its own setting so the default applies again.</summary>
public sealed record SetOfferFeeRequest(int? AmountPaise);

/// <summary>What the person asking would pay to publish an offer: their store's fee (store staff), or nothing (an administrator).</summary>
public sealed record MyOfferFee(int FeePaise, bool PaymentsAvailable, int LoadPaise = 0);

public interface IOfferFeeStore
{
    Task<int> GetDefaultAsync(CancellationToken cancellationToken = default);

    /// <summary>The extra fee for an offer that is also shown when the app opens (one price for every store).</summary>
    Task<int> GetLoadFeeAsync(CancellationToken cancellationToken = default);

    Task SetLoadFeeAsync(int amountPaise, string? by, DateTime now, CancellationToken cancellationToken = default);
    Task<IReadOnlyDictionary<Guid, int>> GetOverridesAsync(CancellationToken cancellationToken = default);

    /// <summary>Sets the default (no store) or one store's own fee; a null amount removes a store's own fee.</summary>
    Task SetAsync(Guid? storeId, int? amountPaise, string? by, DateTime now, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<Store>> GetStoresAsync(Guid organizationId, CancellationToken cancellationToken = default);
}

public interface IOfferFeeService
{
    /// <summary>Administrators: the default and every store's fee.</summary>
    Task<CampaignOperationResult<OfferFeesResponse>> GetAsync(CancellationToken cancellationToken = default);

    Task<CampaignOperationResult<OfferFeesResponse>> SetDefaultAsync(int amountPaise, CancellationToken cancellationToken = default);

    /// <summary>Sets the extra fee for showing an offer when the app opens.</summary>
    Task<CampaignOperationResult<OfferFeesResponse>> SetLoadAsync(int amountPaise, CancellationToken cancellationToken = default);

    /// <summary>Sets one store's own fee, or removes it with null so the default applies.</summary>
    Task<CampaignOperationResult<OfferFeesResponse>> SetStoreAsync(Guid storeId, int? amountPaise, CancellationToken cancellationToken = default);

    /// <summary>Store staff (and administrators): what an offer would cost, before they write one.</summary>
    Task<CampaignOperationResult<MyOfferFee>> GetMineAsync(CancellationToken cancellationToken = default);
}

/// <summary>Paying the fee of an offer, with the same provider and rules as paying for an order.</summary>
public interface ICampaignPaymentService
{
    /// <summary>Starts or resumes paying: returns what the payment screen needs. Asking again reuses the same provider order, so the fee is never charged twice.</summary>
    Task<PaymentOperationResult<StartPaymentResponse>> StartAsync(Guid campaignId, CancellationToken cancellationToken = default);

    /// <summary>Reports the result of the payment screen. The server checks the signature and asks the provider itself. Repeating it changes nothing.</summary>
    Task<PaymentOperationResult<CampaignResponse>> ConfirmAsync(Guid campaignId, ConfirmPaymentRequest request, CancellationToken cancellationToken = default);
}

/// <summary>The upkeep for fee payments: refunds of cancelled offers, and finding payments whose confirmation never arrived.</summary>
public interface ICampaignPaymentMaintenance
{
    Task<int> StartRefundsAsync(CancellationToken cancellationToken = default);
    Task<int> ReconcileAsync(CancellationToken cancellationToken = default);
}

public interface ICampaignPaymentStore
{
    Task<CampaignPayment?> GetPaymentAsync(Guid campaignId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Starts an attempt to pay for an offer that is waiting for its fee: creates the payment row if there is none (the amount is the
    /// offer's own fee), counts the attempt, and puts a failed payment back in progress. Null when the offer is not waiting for payment.
    /// </summary>
    Task<CampaignPayment?> BeginAttemptAsync(Guid campaignId, DateTime now, CancellationToken cancellationToken = default);

    /// <summary>Records the provider's order on the payment; only the first request wins, the rest change nothing.</summary>
    Task<bool> AttachProviderOrderAsync(Guid paymentId, string providerOrderId, CancellationToken cancellationToken = default);

    /// <summary>The provider says this order was paid. Applies it once, to the payment and the offer together.</summary>
    Task<CapturedOutcome> ApplyCapturedAsync(string providerOrderId, string providerPaymentId, long paidPaise, DateTime now, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<CampaignPayment>> GetRefundsToStartAsync(int max, CancellationToken cancellationToken = default);
    Task SetRefundStartedAsync(Guid paymentId, string refundId, bool processed, DateTime now, CancellationToken cancellationToken = default);
    Task SetRefundFailedAsync(Guid paymentId, string reason, bool permanent, DateTime now, CancellationToken cancellationToken = default);

    /// <summary>Payments that have waited long enough for the provider's word: unpaid ones with a provider order, and refunds in progress.</summary>
    Task<IReadOnlyList<CampaignPayment>> GetUnsettledAsync(DateTime olderThan, int max, CancellationToken cancellationToken = default);

    /// <summary>The provider says the refund is done.</summary>
    Task SetRefundedAsync(Guid paymentId, DateTime now, CancellationToken cancellationToken = default);
}
