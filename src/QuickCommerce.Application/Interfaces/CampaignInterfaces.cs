using QuickCommerce.Domain;

namespace QuickCommerce.Application.Interfaces;

/// <summary>An offer or announcement an admin writes. Marathi is optional: customers using Marathi get the English words when it is missing.</summary>
public sealed record CampaignRequest(string TitleEn, string BodyEn, string? TitleMr = null, string? BodyMr = null, DateTime? StartsAt = null);

public sealed record CampaignResponse(
    Guid Id,
    string TitleEn,
    string BodyEn,
    string? TitleMr,
    string? BodyMr,
    DateTime StartsAt,
    string Status,
    int RecipientCount,
    DateTime CreatedAt);

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
    Task<CampaignOperationResult<IReadOnlyList<CampaignResponse>>> ListAsync(CancellationToken cancellationToken = default);
    Task<CampaignOperationResult<CampaignResponse>> CreateAsync(CampaignRequest request, CancellationToken cancellationToken = default);

    /// <summary>Cancels a campaign that has not been sent. One already sent cannot be taken back (409, <see cref="CampaignReasons.AlreadySent"/>); cancelling twice is fine.</summary>
    Task<CampaignOperationResult<CampaignResponse>> CancelAsync(Guid id, CancellationToken cancellationToken = default);
}

public interface ICampaignStore
{
    Task<IReadOnlyList<Campaign>> ListCampaignsAsync(CancellationToken cancellationToken = default);
    Task AddCampaignAsync(Campaign campaign, CancellationToken cancellationToken = default);
    Task<Campaign?> GetCampaignAsync(Guid id, CancellationToken cancellationToken = default);

    /// <summary>Marks a campaign cancelled unless it was already sent. Returns the campaign as it now is, or null when there is none.</summary>
    Task<Campaign?> TryCancelCampaignAsync(Guid id, CancellationToken cancellationToken = default);

    /// <summary>
    /// Sends every campaign whose start time has come and has not been sent: one notification per customer who has offers on, once only
    /// (safe to run on several servers at once). Returns how many campaigns were sent.
    /// </summary>
    Task<int> PublishDueCampaignsAsync(DateTime now, CancellationToken cancellationToken = default);
}

/// <summary>What a customer chose about notifications. Order and payment notifications cannot be switched off.</summary>
public sealed record NotificationPreferences(bool Offers);
