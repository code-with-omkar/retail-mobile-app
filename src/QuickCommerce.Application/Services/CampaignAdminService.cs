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
    TimeProvider? timeProvider = null,
    IOfferFeeStore? fees = null,
    PaymentSettings? payments = null,
    IImageStore? images = null) : ICampaignAdminService
{
    private const int TitleMax = 160;
    private const int BodyMax = 500;
    private const int MaxProducts = 50;
    private const int MaxDiscountDays = 90;
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
        var shown = visible.OrderByDescending(item => item.StartsAt).ToArray();
        var names = await store.GetProductNamesAsync(shown.SelectMany(item => item.Products).Select(link => link.ProductId).Distinct().ToArray(), cancellationToken);
        return new(CampaignOperationStatus.Succeeded, shown.Select(item => CampaignMapping.ToResponse(item, names)).ToArray());
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

        var now = clock.GetUtcNow().UtcDateTime;
        var startsAt = request.StartsAt is { } at ? at.ToUniversalTime() : now;
        // A start time already past means "now".
        startsAt = startsAt < now ? now : startsAt;
        var productIds = (request.ProductIds ?? []).Distinct().ToArray();
        DateTime? endsAt = null;
        if (request.DiscountPercent > 0)
        {
            endsAt = request.EndsAt?.ToUniversalTime();
            if (endsAt is null || endsAt <= startsAt)
            {
                return new(CampaignOperationStatus.InvalidRequest, Message: "A discount needs an end time after its start");
            }

            if (endsAt > startsAt.AddDays(MaxDiscountDays))
            {
                return new(CampaignOperationStatus.InvalidRequest, Message: $"A discount can run for up to {MaxDiscountDays} days");
            }

            var products = await store.GetActiveProductsAsync(productIds, cancellationToken);
            if (products.Count != productIds.Length)
            {
                return new(CampaignOperationStatus.InvalidRequest, Message: "One of the products is not known");
            }
        }

        if (request.ImageUrl is not null && !OfferImages.IsOwnPath(request.ImageUrl))
        {
            return new(CampaignOperationStatus.InvalidRequest, Message: "Upload the picture first and use the path it returns");
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

        // Store staff pay a fee to publish (their store's own setting, else the default); an administrator announces for the business, free.
        var fee = !actor.Admin && fees is not null ? await OfferFeeService.EffectiveAsync(fees, actor.StoreId!.Value, cancellationToken) : 0;
        // Showing the offer when the app opens costs an extra fee the administrator sets.
        if (!actor.Admin && fees is not null && request.ShowOnLoad)
        {
            fee += await fees.GetLoadFeeAsync(cancellationToken);
        }

        if (fee > 0 && payments?.Enabled != true)
        {
            return new(CampaignOperationStatus.Conflict, Message: "Online payment is not available right now, so an offer with a fee cannot be published.", Reason: PaymentReasons.PaymentsUnavailable);
        }

        var campaign = new Campaign
        {
            TitleEn = request.TitleEn.Trim(),
            BodyEn = Clean(request.BodyEn) ?? string.Empty,
            TitleMr = Clean(request.TitleMr),
            BodyMr = Clean(request.BodyMr),
            StartsAt = startsAt,
            CreatedAt = now,
            Stores = wanted.Select(id => new CampaignStore { StoreId = id }).ToList(),
            FeePaise = fee,
            PaymentStatus = fee > 0 ? PaymentState.Created : PaymentState.NotRequired,
            Notify = request.Notify,
            ShowOnLoad = request.ShowOnLoad,
            DiscountPercent = request.DiscountPercent,
            EndsAt = endsAt,
            ImageUrl = request.ImageUrl,
            Products = request.DiscountPercent > 0 ? productIds.Select(id => new CampaignProduct { ProductId = id }).ToList() : []
        };
        await store.AddCampaignAsync(campaign, cancellationToken);
        var saved = await store.GetCampaignAsync(campaign.Id, cancellationToken) ?? campaign;
        return new(CampaignOperationStatus.Succeeded, CampaignMapping.ToResponse(saved, await store.GetProductNamesAsync(productIds, cancellationToken)));
    }

    public async Task<CampaignOperationResult<CampaignResponse>> EndDiscountAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var actor = await ResolveAsync(cancellationToken);
        if (actor is null)
        {
            return new(CampaignOperationStatus.Unauthorized, Message: NotAllowed);
        }

        var existing = await store.GetCampaignAsync(id, cancellationToken);
        if (existing is null || (!actor.Admin && !(existing.Stores.Count == 1 && existing.Stores[0].StoreId == actor.StoreId)))
        {
            return new(CampaignOperationStatus.NotFound, Message: "Offer not found");
        }

        var now = clock.GetUtcNow().UtcDateTime;
        var running = existing.IsActive && existing.DiscountPercent > 0 && existing.EndsAt > now;
        if (!running)
        {
            return new(CampaignOperationStatus.Conflict, Message: "This offer has no discount running.", Reason: CampaignReasons.DiscountAlreadyEnded);
        }

        var ended = await store.TryEndDiscountAsync(id, now, cancellationToken) ?? existing;
        return new(CampaignOperationStatus.Succeeded, CampaignMapping.ToResponse(ended, await store.GetProductNamesAsync(ended.Products.Select(link => link.ProductId).ToArray(), cancellationToken)));
    }

    public async Task<CampaignOperationResult<IReadOnlyList<OfferProductChoice>>> SearchProductsAsync(string? search, CancellationToken cancellationToken = default)
    {
        if (await ResolveAsync(cancellationToken) is null)
        {
            return new(CampaignOperationStatus.Unauthorized, Message: NotAllowed);
        }

        var term = string.IsNullOrWhiteSpace(search) ? null : search.Trim();
        var found = await store.SearchActiveProductsAsync(term?.Length > 80 ? term[..80] : term, 30, cancellationToken);
        return new(CampaignOperationStatus.Succeeded, found.Select(item => new OfferProductChoice(item.Id, item.Name, item.Price, item.ImageUrl)).ToArray());
    }

    public async Task<CampaignOperationResult<string>> UploadImageAsync(Stream content, long length, CancellationToken cancellationToken = default)
    {
        var actor = await ResolveAsync(cancellationToken);
        if (actor is null)
        {
            return new(CampaignOperationStatus.Unauthorized, Message: NotAllowed);
        }

        if (images is null)
        {
            return new(CampaignOperationStatus.Conflict, Message: "Pictures cannot be saved right now.");
        }

        if (length <= 0 || length > CatalogAdminService.MaxImageBytes)
        {
            return new(CampaignOperationStatus.InvalidRequest, Message: "The picture must be 2 MB or smaller.");
        }

        using var buffer = new MemoryStream();
        await content.CopyToAsync(buffer, cancellationToken);
        var bytes = buffer.ToArray();
        var extension = CatalogAdminService.ImageType(bytes);
        if (bytes.Length > CatalogAdminService.MaxImageBytes || extension is null)
        {
            return new(CampaignOperationStatus.InvalidRequest, Message: bytes.Length > CatalogAdminService.MaxImageBytes ? "The picture must be 2 MB or smaller." : "Use a JPEG, PNG or WebP picture.");
        }

        // The name is made here, never taken from the upload.
        var path = await images.SaveAsync(OfferImages.Folder, $"{Guid.NewGuid():N}", extension, bytes, cancellationToken);
        return new(CampaignOperationStatus.Succeeded, path);
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
            ? new(CampaignOperationStatus.Conflict, CampaignMapping.ToResponse(campaign), "This offer was already sent and cannot be taken back.", CampaignReasons.AlreadySent)
            : new(CampaignOperationStatus.Succeeded, CampaignMapping.ToResponse(campaign));
    }

    private static string? Clean(string? text) => string.IsNullOrWhiteSpace(text) ? null : text.Trim();

    private string? Validate(CampaignRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.TitleEn) || (request.Notify && string.IsNullOrWhiteSpace(request.BodyEn)))
        {
            return request.Notify ? "An offer needs an English title and message" : "An offer needs an English title";
        }

        if (request.DiscountPercent is < 0 or > OfferPricing.MaxPercent)
        {
            return $"A discount is between 1 and {OfferPricing.MaxPercent} percent";
        }

        if (request.DiscountPercent == 0 && !request.Notify)
        {
            return "An offer that sends no notification needs a discount";
        }

        if (request.DiscountPercent > 0 && ((request.ProductIds?.Count ?? 0) is 0 or > MaxProducts))
        {
            return $"Choose between 1 and {MaxProducts} products for the discount";
        }

        if (request.TitleEn.Trim().Length > TitleMax || (Clean(request.BodyEn)?.Length ?? 0) > BodyMax || (Clean(request.TitleMr)?.Length ?? 0) > TitleMax || (Clean(request.BodyMr)?.Length ?? 0) > BodyMax)
        {
            return $"A title can be up to {TitleMax} characters and a message up to {BodyMax}";
        }

        if (request.Notify && (Clean(request.TitleMr) is null) != (Clean(request.BodyMr) is null))
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
}
