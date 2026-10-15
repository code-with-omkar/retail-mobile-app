using QuickCommerce.Domain;

namespace QuickCommerce.Application.Interfaces;

/// <summary>
/// An offer or announcement. Marathi is optional: customers using Marathi get the English words when it is missing.
/// <see cref="StoreIds"/> says which stores it is from: store staff always announce for their own store (empty means their store); an
/// administrator picks the stores, and empty means every store.
/// </summary>
/// <remarks>
/// An offer is a message (sent to phones when <see cref="Notify"/>), a discount, or both. A discount is <see cref="DiscountPercent"/> off the
/// <see cref="ProductIds"/> from the start until <see cref="EndsAt"/>; it can run quietly with <see cref="Notify"/> false (then the message is optional).
/// <see cref="ImageUrl"/> is a path returned by the image upload.
/// </remarks>
public sealed record CampaignRequest(
    string TitleEn,
    string? BodyEn = null,
    string? TitleMr = null,
    string? BodyMr = null,
    DateTime? StartsAt = null,
    IReadOnlyList<Guid>? StoreIds = null,
    string? ImageUrl = null,
    int DiscountPercent = 0,
    DateTime? EndsAt = null,
    IReadOnlyList<Guid>? ProductIds = null,
    bool Notify = true,
    bool ShowOnLoad = false);

public sealed record CampaignStoreInfo(Guid Id, string Name);

public sealed record CampaignProductInfo(Guid Id, string Name);

/// <summary>A product to pick for a discount.</summary>
public sealed record OfferProductChoice(Guid Id, string Name, decimal Price, string? ImageUrl);

public sealed record CampaignResponse(
    Guid Id,
    string TitleEn,
    string BodyEn,
    string? TitleMr,
    string? BodyMr,
    DateTime StartsAt,
    string Status,
    int RecipientCount,
    DateTime CreatedAt,
    IReadOnlyList<CampaignStoreInfo> Stores,
    bool AllStores,
    int FeePaise = 0,
    PaymentState PaymentStatus = PaymentState.NotRequired,
    string? ImageUrl = null,
    int DiscountPercent = 0,
    DateTime? EndsAt = null,
    bool Notify = true,
    IReadOnlyList<CampaignProductInfo>? Products = null,
    bool ShowOnLoad = false);

/// <summary>Where a campaign is: waiting for its start time, sent (to <see cref="CampaignResponse.RecipientCount"/> customers) or cancelled before it was sent.</summary>
public static class CampaignStatuses
{
    public const string Scheduled = "Scheduled";

    /// <summary>An offer with a fee that has not been paid yet: it is sent only once it is paid.</summary>
    public const string AwaitingPayment = "AwaitingPayment";
    public const string Sent = "Sent";
    public const string Cancelled = "Cancelled";
}

public enum CampaignOperationStatus
{
    Succeeded,
    InvalidRequest,
    Unauthorized,
    NotFound,
    Conflict
}

public sealed record CampaignOperationResult<T>(CampaignOperationStatus Status, T? Value = default, string? Message = null, string? Reason = null);

public static class CampaignReasons
{
    public const string AlreadySent = "CampaignAlreadySent";
    public const string NotAwaitingPayment = "OfferNotAwaitingPayment";
    public const string DiscountAlreadyEnded = "OfferDiscountEnded";
}

public interface ICampaignAdminService
{
    /// <summary>Administrators see every offer; store staff see the offers that are for their store.</summary>
    Task<CampaignOperationResult<IReadOnlyList<CampaignResponse>>> ListAsync(CancellationToken cancellationToken = default);

    Task<CampaignOperationResult<CampaignResponse>> CreateAsync(CampaignRequest request, CancellationToken cancellationToken = default);

    /// <summary>
    /// Cancels an offer that has not been sent. One already sent cannot be taken back (409, <see cref="CampaignReasons.AlreadySent"/>);
    /// cancelling twice is fine. Store staff can cancel only offers that are for their store alone.
    /// </summary>
    Task<CampaignOperationResult<CampaignResponse>> CancelAsync(Guid id, CancellationToken cancellationToken = default);

    /// <summary>Ends a running discount now (the message already sent stays sent). 409 when it has no discount or the discount is not running.</summary>
    Task<CampaignOperationResult<CampaignResponse>> EndDiscountAsync(Guid id, CancellationToken cancellationToken = default);

    /// <summary>Active products whose name contains the text, up to 30, for the discount's product picker.</summary>
    Task<CampaignOperationResult<IReadOnlyList<OfferProductChoice>>> SearchProductsAsync(string? search, CancellationToken cancellationToken = default);

    /// <summary>Saves a banner picture (JPEG, PNG or WebP, up to 2 MB, judged by content) and returns its path for <see cref="CampaignRequest.ImageUrl"/>.</summary>
    Task<CampaignOperationResult<string>> UploadImageAsync(Stream content, long length, CancellationToken cancellationToken = default);
}

public interface ICampaignStore
{
    /// <summary>Every campaign with the stores it is for.</summary>
    Task<IReadOnlyList<Campaign>> ListCampaignsAsync(CancellationToken cancellationToken = default);

    Task AddCampaignAsync(Campaign campaign, CancellationToken cancellationToken = default);
    Task<Campaign?> GetCampaignAsync(Guid id, CancellationToken cancellationToken = default);

    /// <summary>The active stores of the organization with these ids (an unknown or other organization's id is simply missing).</summary>
    Task<IReadOnlyList<Store>> GetStoresAsync(IReadOnlyCollection<Guid> ids, Guid organizationId, CancellationToken cancellationToken = default);

    /// <summary>Marks a campaign cancelled unless it was already sent. A fee already paid is queued for a refund; an unpaid one is closed. Returns the campaign as it now is, or null when there is none.</summary>
    Task<Campaign?> TryCancelCampaignAsync(Guid id, CancellationToken cancellationToken = default);

    /// <summary>The active products with these ids (a made-up or switched-off product is simply missing).</summary>
    Task<IReadOnlyList<Product>> GetActiveProductsAsync(IReadOnlyCollection<Guid> ids, CancellationToken cancellationToken = default);

    /// <summary>Stops a discount that is running by setting its end to <paramref name="now"/>. Returns the campaign as it now is, or null when there is none.</summary>
    Task<Campaign?> TryEndDiscountAsync(Guid id, DateTime now, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<Product>> SearchActiveProductsAsync(string? search, int take, CancellationToken cancellationToken = default);

    /// <summary>Product names for the offer list.</summary>
    Task<IReadOnlyDictionary<Guid, string>> GetProductNamesAsync(IReadOnlyCollection<Guid> ids, CancellationToken cancellationToken = default);

    /// <summary>
    /// Sends every campaign whose start time has come, that has not been sent and (when it has a fee) is paid: one notification per customer who has offers on, once only
    /// (safe to run on several servers at once). An offer for chosen stores goes to the customers of those stores (who have ordered from
    /// one of them or have shopped there); an offer for no store in particular goes to every customer. Returns how many campaigns were sent.
    /// </summary>
    Task<int> PublishDueCampaignsAsync(DateTime now, CancellationToken cancellationToken = default);
}

/// <summary>What a customer chose about notifications. Order and payment notifications cannot be switched off.</summary>
public sealed record NotificationPreferences(bool Offers);

/// <summary>An offer that is running at a store, for the customer's Home banner.</summary>
public sealed record ActiveOffer(Guid Id, string TitleEn, string? BodyEn, string? TitleMr, string? BodyMr, string? ImageUrl, int DiscountPercent, DateTime? EndsAt, IReadOnlyList<Guid> ProductIds, bool ShowOnLoad = false);

/// <summary>One offer opened from a notification or a banner. Anyone can read an offer that has started and was not cancelled; its fee state is never shown.</summary>
public sealed record OfferDetail(
    Guid Id,
    string TitleEn,
    string? BodyEn,
    string? TitleMr,
    string? BodyMr,
    string? ImageUrl,
    int DiscountPercent,
    DateTime? EndsAt,
    bool Running,
    IReadOnlyList<CampaignStoreInfo> Stores,
    IReadOnlyList<Guid> ProductIds);

/// <summary>What is on offer at a store right now. A discount runs from its start to its end, only when it is not cancelled and (with a fee) is paid.</summary>
public interface IOfferDiscountLookup
{
    /// <summary>The percent off for each of these products at the store; products with no discount are missing. Where several offers cover a product the highest wins (they do not add up).</summary>
    Task<IReadOnlyDictionary<Guid, int>> GetPercentsAsync(Guid storeId, IReadOnlyCollection<Guid> productIds, DateTime now, CancellationToken cancellationToken = default);

    /// <summary>One offer by id, or null when there is none, it has not started, it was cancelled or its fee is unpaid.</summary>
    Task<OfferDetail?> GetOfferAsync(Guid id, DateTime now, CancellationToken cancellationToken = default);

    /// <summary>The offers to show on Home: running discounts, and sent messages that have a picture, newest first.</summary>
    Task<IReadOnlyList<ActiveOffer>> GetActiveOffersAsync(Guid storeId, DateTime now, CancellationToken cancellationToken = default);
}
