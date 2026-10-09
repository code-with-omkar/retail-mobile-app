using QuickCommerce.Application.Interfaces;
using QuickCommerce.Domain;

namespace QuickCommerce.Application.Services;

public sealed class CampaignAdminService(
    ICampaignStore store,
    IAuthorizationScopeService scopeService,
    TimeProvider? timeProvider = null) : ICampaignAdminService
{
    private const int TitleMax = 160;
    private const int BodyMax = 500;
    private readonly TimeProvider clock = timeProvider ?? TimeProvider.System;

    public async Task<CampaignOperationResult<IReadOnlyList<CampaignResponse>>> ListAsync(CancellationToken cancellationToken = default)
    {
        if (!await IsAdminAsync(cancellationToken))
        {
            return new(CampaignOperationStatus.Unauthorized, Message: "Only an administrator can manage offers");
        }

        var campaigns = await store.ListCampaignsAsync(cancellationToken);
        return new(CampaignOperationStatus.Succeeded, campaigns.OrderByDescending(item => item.StartsAt).Select(Map).ToArray());
    }

    public async Task<CampaignOperationResult<CampaignResponse>> CreateAsync(CampaignRequest request, CancellationToken cancellationToken = default)
    {
        if (!await IsAdminAsync(cancellationToken))
        {
            return new(CampaignOperationStatus.Unauthorized, Message: "Only an administrator can manage offers");
        }

        var problem = Validate(request);
        if (problem is not null)
        {
            return new(CampaignOperationStatus.InvalidRequest, Message: problem);
        }

        var now = clock.GetUtcNow().UtcDateTime;
        var startsAt = request.StartsAt is { } wanted ? wanted.ToUniversalTime() : now;
        var campaign = new Campaign
        {
            TitleEn = request.TitleEn.Trim(),
            BodyEn = request.BodyEn.Trim(),
            TitleMr = Clean(request.TitleMr),
            BodyMr = Clean(request.BodyMr),
            // A start time already past means "now".
            StartsAt = startsAt < now ? now : startsAt,
            CreatedAt = now
        };
        await store.AddCampaignAsync(campaign, cancellationToken);
        return new(CampaignOperationStatus.Succeeded, Map(campaign));
    }

    public async Task<CampaignOperationResult<CampaignResponse>> CancelAsync(Guid id, CancellationToken cancellationToken = default)
    {
        if (!await IsAdminAsync(cancellationToken))
        {
            return new(CampaignOperationStatus.Unauthorized, Message: "Only an administrator can manage offers");
        }

        var campaign = await store.TryCancelCampaignAsync(id, cancellationToken);
        if (campaign is null)
        {
            return new(CampaignOperationStatus.NotFound, Message: "Offer not found");
        }

        return campaign.PublishedAt is not null
            ? new(CampaignOperationStatus.Conflict, Map(campaign), "This offer was already sent and cannot be taken back.", CampaignReasons.AlreadySent)
            : new(CampaignOperationStatus.Succeeded, Map(campaign));
    }

    private async Task<bool> IsAdminAsync(CancellationToken cancellationToken) => (await scopeService.ResolveAsync(cancellationToken))?.IsApplicationAdmin == true;

    private static string? Clean(string? text) => string.IsNullOrWhiteSpace(text) ? null : text.Trim();

    private string? Validate(CampaignRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.TitleEn) || string.IsNullOrWhiteSpace(request.BodyEn))
        {
            return "An offer needs an English title and message";
        }

        if (request.TitleEn.Trim().Length > TitleMax || request.BodyEn.Trim().Length > BodyMax || (Clean(request.TitleMr)?.Length ?? 0) > TitleMax || (Clean(request.BodyMr)?.Length ?? 0) > BodyMax)
        {
            return $"A title can be up to {TitleMax} characters and a message up to {BodyMax}";
        }

        if ((Clean(request.TitleMr) is null) != (Clean(request.BodyMr) is null))
        {
            return "Give both the Marathi title and message, or neither";
        }

        if (request.StartsAt is { } startsAt && startsAt.ToUniversalTime() > clock.GetUtcNow().UtcDateTime.AddYears(1))
        {
            return "The start time is too far ahead";
        }

        return null;
    }

    private static CampaignResponse Map(Campaign campaign) => new(
        campaign.Id,
        campaign.TitleEn,
        campaign.BodyEn,
        campaign.TitleMr,
        campaign.BodyMr,
        DateTime.SpecifyKind(campaign.StartsAt, DateTimeKind.Utc),
        !campaign.IsActive ? CampaignStatuses.Cancelled : campaign.PublishedAt is null ? CampaignStatuses.Scheduled : CampaignStatuses.Sent,
        campaign.RecipientCount,
        DateTime.SpecifyKind(campaign.CreatedAt, DateTimeKind.Utc));
}
