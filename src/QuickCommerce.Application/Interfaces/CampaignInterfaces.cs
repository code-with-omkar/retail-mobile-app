using QuickCommerce.Domain;

namespace QuickCommerce.Application.Interfaces;

/// <summary>
/// An offer or announcement. Marathi is optional: customers using Marathi get the English words when it is missing.
/// <see cref="StoreIds"/> says which stores it is from: store staff always announce for their own store (empty means their store); an
/// administrator picks the stores, and empty means every store.
/// </summary>
public sealed record CampaignRequest(string TitleEn, string BodyEn, string? TitleMr = null, string? BodyMr = null, DateTime? StartsAt = null, IReadOnlyList<Guid>? StoreIds = null);

public sealed record CampaignStoreInfo(Guid Id, string Name);

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
    bool AllStores);

/// <summary>Where a campaign is: waiting for its start time, sent (to <see cref="CampaignResponse.RecipientCount"/> customers) or cancelled before it was sent.</summary>
public static class CampaignStatuses
{
    public const string Scheduled = "Scheduled";
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
}

public interface ICampaignStore
{
    /// <summary>Every campaign with the stores it is for.</summary>
    Task<IReadOnlyList<Campaign>> ListCampaignsAsync(CancellationToken cancellationToken = default);

    Task AddCampaignAsync(Campaign campaign, CancellationToken cancellationToken = default);
    Task<Campaign?> GetCampaignAsync(Guid id, CancellationToken cancellationToken = default);

    /// <summary>The active stores of the organization with these ids (an unknown or other organization's id is simply missing).</summary>
    Task<IReadOnlyList<Store>> GetStoresAsync(IReadOnlyCollection<Guid> ids, Guid organizationId, CancellationToken cancellationToken = default);

    /// <summary>Marks a campaign cancelled unless it was already sent. Returns the campaign as it now is, or null when there is none.</summary>
    Task<Campaign?> TryCancelCampaignAsync(Guid id, CancellationToken cancellationToken = default);

    /// <summary>
    /// Sends every campaign whose start time has come and has not been sent: one notification per customer who has offers on, once only
    /// (safe to run on several servers at once). An offer for chosen stores goes to the customers of those stores (who have ordered from
    /// one of them or have shopped there); an offer for no store in particular goes to every customer. Returns how many campaigns were sent.
    /// </summary>
    Task<int> PublishDueCampaignsAsync(DateTime now, CancellationToken cancellationToken = default);
}

/// <summary>What a customer chose about notifications. Order and payment notifications cannot be switched off.</summary>
public sealed record NotificationPreferences(bool Offers);
