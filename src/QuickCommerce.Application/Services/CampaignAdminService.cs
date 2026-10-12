using QuickCommerce.Application.Interfaces;
using QuickCommerce.Domain;

namespace QuickCommerce.Application.Services;

/// <summary>
/// Offers for customers. An administrator can create an offer for chosen stores (or all) and manage every offer; store staff announce
/// offers for their own store only and see and cancel only those that are for their store. Anything else about another store is "not found".
/// </summary>
public sealed class CampaignAdminService(
    ICampaignStore store,
    IAuthorizationScopeService scopeService,
    ICurrentUserContextResolver userContext,
    TimeProvider? timeProvider = null) : ICampaignAdminService
{
    private const int TitleMax = 160;
    private const int BodyMax = 500;
    private const string NotAllowed = "Only an administrator or a store can manage offers";
    private readonly TimeProvider clock = timeProvider ?? TimeProvider.System;

    /// <summary>Who is asking: an administrator (any store), or store staff of one store.</summary>
    private sealed record Actor(bool Admin, Guid OrganizationId, Guid? StoreId);

    private async Task<Actor?> ResolveAsync(CancellationToken cancellationToken)
    {
        var scope = await scopeService.ResolveAsync(cancellationToken);
        if (scope?.IsApplicationAdmin == true)
        {
            return new Actor(true, scope.OrganizationId, null);
        }

        var context = await userContext.ResolveAsync(cancellationToken);
        return context is { Role: Role.StoreStaff, StoreId: { } storeId } ? new Actor(false, context.OrganizationId, storeId) : null;
    }

    public async Task<CampaignOperationResult<IReadOnlyList<CampaignResponse>>> ListAsync(CancellationToken cancellationToken = default)
    {
        var actor = await ResolveAsync(cancellationToken);
        if (actor is null)
        {
            return new(CampaignOperationStatus.Unauthorized, Message: NotAllowed);
        }

        var campaigns = await store.ListCampaignsAsync(cancellationToken);
        var visible = actor.Admin ? campaigns : campaigns.Where(campaign => campaign.Stores.Any(link => link.StoreId == actor.StoreId));
        return new(CampaignOperationStatus.Succeeded, visible.OrderByDescending(item => item.StartsAt).Select(Map).ToArray());
    }

    public async Task<CampaignOperationResult<CampaignResponse>> CreateAsync(CampaignRequest request, CancellationToken cancellationToken = default)
    {
        var actor = await ResolveAsync(cancellationToken);
        if (actor is null)
        {
            return new(CampaignOperationStatus.Unauthorized, Message: NotAllowed);
        }

        var problem = Validate(request);
        if (problem is not null)
        {
            return new(CampaignOperationStatus.InvalidRequest, Message: problem);
        }

        var wanted = (request.StoreIds ?? []).Distinct().ToArray();
        if (!actor.Admin)
        {
            // Store staff announce for their own store, and only that.
            if (wanted.Length > 0 && (wanted.Length != 1 || wanted[0] != actor.StoreId))
            {
                return new(CampaignOperationStatus.Unauthorized, Message: "You can only announce offers for your own store");
            }

            wanted = [actor.StoreId!.Value];
        }

        if (wanted.Length > 0)
        {
            var found = await store.GetStoresAsync(wanted, actor.OrganizationId, cancellationToken);
            if (found.Count != wanted.Length)
            {
                return new(CampaignOperationStatus.InvalidRequest, Message: "One of the stores is not known");
            }
        }

        var now = clock.GetUtcNow().UtcDateTime;
        var startsAt = request.StartsAt is { } at ? at.ToUniversalTime() : now;
        var campaign = new Campaign
        {
            TitleEn = request.TitleEn.Trim(),
            BodyEn = request.BodyEn.Trim(),
            TitleMr = Clean(request.TitleMr),
            BodyMr = Clean(request.BodyMr),
            // A start time already past means "now".
            StartsAt = startsAt < now ? now : startsAt,
            CreatedAt = now,
            Stores = wanted.Select(id => new CampaignStore { StoreId = id }).ToList()
        };
        await store.AddCampaignAsync(campaign, cancellationToken);
        var saved = await store.GetCampaignAsync(campaign.Id, cancellationToken) ?? campaign;
        return new(CampaignOperationStatus.Succeeded, Map(saved));
    }

    public async Task<CampaignOperationResult<CampaignResponse>> CancelAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var actor = await ResolveAsync(cancellationToken);
        if (actor is null)
        {
            return new(CampaignOperationStatus.Unauthorized, Message: NotAllowed);
        }

        var existing = await store.GetCampaignAsync(id, cancellationToken);
        // Another store's offer, or an offer for several stores, is not the store's to cancel: it looks like there is none.
        if (existing is null || (!actor.Admin && !(existing.Stores.Count == 1 && existing.Stores[0].StoreId == actor.StoreId)))
        {
            return new(CampaignOperationStatus.NotFound, Message: "Offer not found");
        }

        var campaign = await store.TryCancelCampaignAsync(id, cancellationToken) ?? existing;
        return campaign.PublishedAt is not null
            ? new(CampaignOperationStatus.Conflict, Map(campaign), "This offer was already sent and cannot be taken back.", CampaignReasons.AlreadySent)
            : new(CampaignOperationStatus.Succeeded, Map(campaign));
    }

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

        if ((request.StoreIds?.Count ?? 0) > 200)
        {
            return "Too many stores";
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
        DateTime.SpecifyKind(campaign.CreatedAt, DateTimeKind.Utc),
        campaign.Stores.Select(link => new CampaignStoreInfo(link.StoreId, link.Store?.Name ?? string.Empty)).OrderBy(info => info.Name).ToArray(),
        campaign.Stores.Count == 0);
}
