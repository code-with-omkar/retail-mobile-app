using QuickCommerce.Application.Interfaces;
using QuickCommerce.Domain;

namespace QuickCommerce.Application.Services;

/// <summary>Ads: made by administrators, shown to every customer, counted when seen and tapped.</summary>
public sealed class AdService(IAdStore ads, IAuthorizationScopeService scopeService, TimeProvider? timeProvider = null) : IAdService
{
    private const string NotAllowed = "Only an administrator can manage ads.";
    private const int ShownPerPlace = 5;
    private readonly TimeProvider clock = timeProvider ?? TimeProvider.System;

    public async Task<AdResult<IReadOnlyList<AdResponse>>> ListAsync(CancellationToken cancellationToken = default)
    {
        var scope = await scopeService.ResolveAsync(cancellationToken);
        if (scope?.IsApplicationAdmin != true)
        {
            return new(AdServiceStatus.Forbidden, Message: NotAllowed);
        }

        var now = clock.GetUtcNow().UtcDateTime;
        var list = await ads.ListAsync(scope.OrganizationId, cancellationToken);
        return new(AdServiceStatus.Succeeded, list.OrderBy(item => item.Placement).ThenBy(item => item.SortOrder).ThenByDescending(item => item.StartsAt).Select(item => Map(item, now)).ToArray());
    }

    public async Task<AdResult<AdResponse>> CreateAsync(SaveAdRequest request, CancellationToken cancellationToken = default)
    {
        var scope = await scopeService.ResolveAsync(cancellationToken);
        if (scope?.IsApplicationAdmin != true)
        {
            return new(AdServiceStatus.Forbidden, Message: NotAllowed);
        }

        var now = clock.GetUtcNow().UtcDateTime;
        var startsAt = request.StartsAt ?? now;
        if (await ProblemAsync(scope.OrganizationId, request, startsAt, cancellationToken) is { } problem)
        {
            return new(AdServiceStatus.Invalid, Message: problem);
        }

        var ad = new Ad { OrganizationId = scope.OrganizationId, Placement = request.Placement, ImageUrl = request.ImageUrl, CreatedAt = now, CreatedBy = scope.UserId.ToString() };
        Fill(ad, request, startsAt);
        await ads.AddAsync(ad, cancellationToken);
        return new(AdServiceStatus.Succeeded, Map(ad, now));
    }

    public async Task<AdResult<AdResponse>> UpdateAsync(Guid id, SaveAdRequest request, CancellationToken cancellationToken = default)
    {
        var scope = await scopeService.ResolveAsync(cancellationToken);
        if (scope?.IsApplicationAdmin != true)
        {
            return new(AdServiceStatus.Forbidden, Message: NotAllowed);
        }

        var ad = await ads.GetAsync(scope.OrganizationId, id, cancellationToken);
        if (ad is null)
        {
            return new(AdServiceStatus.NotFound, Message: "Ad not found.");
        }

        var now = clock.GetUtcNow().UtcDateTime;
        var startsAt = request.StartsAt ?? ad.StartsAt;
        if (await ProblemAsync(scope.OrganizationId, request, startsAt, cancellationToken) is { } problem)
        {
            return new(AdServiceStatus.Invalid, Message: problem);
        }

        ad.Placement = request.Placement;
        ad.ImageUrl = request.ImageUrl;
        Fill(ad, request, startsAt);
        ad.UpdatedAt = now;
        await ads.SaveAsync(ad, cancellationToken);
        return new(AdServiceStatus.Succeeded, Map(ad, now));
    }

    public async Task<AdResult<bool>> DeleteAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var scope = await scopeService.ResolveAsync(cancellationToken);
        if (scope?.IsApplicationAdmin != true)
        {
            return new(AdServiceStatus.Forbidden, Message: NotAllowed);
        }

        return await ads.DeleteAsync(scope.OrganizationId, id, cancellationToken)
            ? new(AdServiceStatus.Succeeded, true)
            : new(AdServiceStatus.NotFound, Message: "Ad not found.");
    }

    public async Task<IReadOnlyList<AdPublic>> ForPlacementAsync(string? placement, Guid? storeId, CancellationToken cancellationToken = default)
    {
        if (placement is null || !AdPlacements.All.Contains(placement))
        {
            return [];
        }

        var live = await ads.LiveAsync(placement, storeId, clock.GetUtcNow().UtcDateTime, ShownPerPlace, cancellationToken);
        return live.Select(item => new AdPublic(item.Id, item.Placement, item.Title, item.ImageUrl, item.TargetType, item.Target)).ToArray();
    }

    public async Task RecordViewAsync(Guid id, CancellationToken cancellationToken = default) => await ads.CountViewAsync(id, cancellationToken);

    public async Task RecordTapAsync(Guid id, CancellationToken cancellationToken = default) => await ads.CountTapAsync(id, cancellationToken);

    private async Task<string?> ProblemAsync(Guid organizationId, SaveAdRequest request, DateTime startsAt, CancellationToken cancellationToken)
    {
        if (AdRules.Problem(request, startsAt) is { } problem)
        {
            return problem;
        }

        return request.StoreId is { } storeId && !await ads.StoreBelongsAsync(organizationId, storeId, cancellationToken) ? "That store is not one of yours." : null;
    }

    private static void Fill(Ad ad, SaveAdRequest request, DateTime startsAt)
    {
        ad.Title = string.IsNullOrWhiteSpace(request.Title) ? null : request.Title.Trim();
        ad.TargetType = request.TargetType;
        ad.Target = AdRules.NormalizeTarget(request.TargetType, request.Target);
        ad.StoreId = request.StoreId;
        ad.StartsAt = startsAt;
        ad.EndsAt = request.EndsAt;
        ad.IsActive = request.IsActive;
        ad.SortOrder = request.SortOrder;
    }

    private static AdResponse Map(Ad ad, DateTime now) =>
        new(ad.Id, ad.Placement, ad.Title, ad.ImageUrl, ad.TargetType, ad.Target, ad.StoreId, ad.StartsAt, ad.EndsAt, ad.IsActive, ad.SortOrder, ad.ViewCount, ad.ClickCount, AdRules.Status(ad, now));
}
