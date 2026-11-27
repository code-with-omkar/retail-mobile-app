using QuickCommerce.Domain;

namespace QuickCommerce.Application.Interfaces;

public sealed record PlacementRulesResponse(bool Enabled, long MinBidPaise, long MinStepPaise, int MaxWeeksAhead);

public sealed record SetPlacementRulesRequest(bool Enabled, long MinBidPaise, long MinStepPaise, int MaxWeeksAhead);

/// <summary>One upcoming week of a pincode: what leads it now, and the least a new bid must be.</summary>
/// <param name="ClosesAt">When bidding for the week closes: the moment it starts (Monday 00:00 India time).</param>
/// <param name="LeadingPaise">The highest paid bid, or null when there is none yet.</param>
public sealed record PlacementWeekInfo(DateOnly WeekStart, DateTime ClosesAt, long? LeadingPaise, bool LeadingIsMine, long MinNextBidPaise);

public sealed record PlacementMarketResponse(string Pincode, IReadOnlyList<PlacementWeekInfo> Weeks);

/// <param name="StoreId">Only for an administrator bidding on a store's behalf; store staff always bid for their own store.</param>
public sealed record PlaceBidRequest(string Pincode, DateOnly WeekStart, long AmountPaise, Guid? StoreId = null);

public sealed record PlacementBidResponse(
    Guid Id,
    Guid StoreId,
    string StoreName,
    string Pincode,
    DateOnly WeekStart,
    DateTime ClosesAt,
    long AmountPaise,
    string Status,
    PaymentState PaymentStatus,
    DateTime CreatedAt);

public static class PlacementReasons
{
    public const string Disabled = "PlacementDisabled";
    public const string TooLow = "BidTooLow";
    public const string Closed = "BiddingClosed";
    public const string NotAwaitingPayment = "BidNotAwaitingPayment";
    public const string Outbid = "BidOutbid";
    public const string TooManyOpen = "TooManyOpenBids";
}

/// <summary>What happened when the provider reported a bid's payment.</summary>
public enum PlacementCaptureOutcome
{
    /// <summary>It is now the highest paid bid and leads the pincode for the week.</summary>
    Leading,

    /// <summary>A higher paid bid was already there: this payment is refunded.</summary>
    Outbid,

    /// <summary>The week had started or the bid was cancelled: this payment is refunded.</summary>
    Late,

    /// <summary>The wrong amount was paid: refunded.</summary>
    AmountMismatch,

    /// <summary>Seen before: nothing changed.</summary>
    AlreadyPaid,

    UnknownPayment
}

public interface IPlacementService
{
    Task<CampaignOperationResult<PlacementRulesResponse>> GetRulesAsync(CancellationToken cancellationToken = default);

    /// <summary>Administrators: sets the minimum bid, the minimum step, how many weeks ahead and whether paid placement is on.</summary>
    Task<CampaignOperationResult<PlacementRulesResponse>> SetRulesAsync(SetPlacementRulesRequest request, CancellationToken cancellationToken = default);

    /// <summary>The upcoming weeks of a pincode: what leads each and the least to bid.</summary>
    Task<CampaignOperationResult<PlacementMarketResponse>> GetMarketAsync(string pincode, CancellationToken cancellationToken = default);

    /// <summary>Places a bid that waits for its payment. It leads only once paid, and only while it is the highest paid bid.</summary>
    Task<CampaignOperationResult<PlacementBidResponse>> PlaceBidAsync(PlaceBidRequest request, CancellationToken cancellationToken = default);

    /// <summary>Store staff: their store's bids; administrators: every bid. Newest first.</summary>
    Task<CampaignOperationResult<IReadOnlyList<PlacementBidResponse>>> ListBidsAsync(CancellationToken cancellationToken = default);

    /// <summary>Store staff can withdraw a bid that is not paid yet; an administrator can also cancel a paid one (refunded).</summary>
    Task<CampaignOperationResult<PlacementBidResponse>> CancelBidAsync(Guid bidId, CancellationToken cancellationToken = default);

    Task<PaymentOperationResult<StartPaymentResponse>> StartPaymentAsync(Guid bidId, CancellationToken cancellationToken = default);
    Task<PaymentOperationResult<PlacementBidResponse>> ConfirmPaymentAsync(Guid bidId, ConfirmPaymentRequest request, CancellationToken cancellationToken = default);
}

/// <summary>For the customer's store list: which store leads a pincode this week.</summary>
public interface IPlacementLookup
{
    Task<Guid?> GetLeadingStoreAsync(string pincode, DateTime nowUtc, CancellationToken cancellationToken = default);
}

public interface IPlacementMaintenance
{
    Task<int> StartRefundsAsync(CancellationToken cancellationToken = default);
    Task<int> ReconcileAsync(CancellationToken cancellationToken = default);
}

public interface IPlacementStore : IPlacementLookup
{
    Task<PlacementRules> GetRulesAsync(CancellationToken cancellationToken = default);
    Task SetRulesAsync(PlacementRules rules, CancellationToken cancellationToken = default);

    Task<Store?> GetStoreAsync(Guid storeId, Guid organizationId, CancellationToken cancellationToken = default);

    /// <summary>The leading bid of a pincode in a week, if any.</summary>
    Task<PlacementBid?> GetLeadingAsync(string pincode, DateOnly weekStart, CancellationToken cancellationToken = default);

    Task<PlacementBid?> GetBidAsync(Guid bidId, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<PlacementBid>> ListBidsAsync(Guid? storeId, int max, CancellationToken cancellationToken = default);
    Task<int> CountOpenBidsAsync(Guid storeId, CancellationToken cancellationToken = default);
    Task AddBidAsync(PlacementBid bid, CancellationToken cancellationToken = default);

    /// <summary>Cancels a bid: an unpaid one is closed, a paid leading one is queued for a refund. Returns the bid as it now is.</summary>
    Task<PlacementBid?> CancelBidAsync(Guid bidId, DateTime now, CancellationToken cancellationToken = default);

    Task<PlacementBid?> BeginAttemptAsync(Guid bidId, DateTime now, CancellationToken cancellationToken = default);
    Task<bool> AttachProviderOrderAsync(Guid bidId, string providerOrderId, CancellationToken cancellationToken = default);

    /// <summary>The provider says this order was paid. Applies it once: the bid leads, is outbid, or is refunded; a beaten leader is queued for a refund.</summary>
    Task<PlacementCaptureOutcome> ApplyCapturedAsync(string providerOrderId, string providerPaymentId, long paidPaise, DateTime now, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<PlacementBid>> GetRefundsToStartAsync(int max, CancellationToken cancellationToken = default);
    Task SetRefundStartedAsync(Guid bidId, string refundId, bool processed, DateTime now, CancellationToken cancellationToken = default);
    Task SetRefundFailedAsync(Guid bidId, string reason, bool permanent, DateTime now, CancellationToken cancellationToken = default);
    Task SetRefundedAsync(Guid bidId, DateTime now, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<PlacementBid>> GetUnsettledAsync(DateTime olderThan, int max, CancellationToken cancellationToken = default);
}
