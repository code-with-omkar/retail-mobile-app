using Microsoft.EntityFrameworkCore;
using QuickCommerce.Application.Interfaces;
using QuickCommerce.Application.Services;
using QuickCommerce.Domain;

namespace QuickCommerce.Infrastructure.Persistence;

/// <summary>Campaigns on SQL Server. Sending is claimed with one conditional update, so only one server sends a campaign, and each customer gets it once.</summary>
public sealed class EfCampaignStore(QuickCommerceDbContext db) : ICampaignStore, IOfferDiscountLookup
{
    private IQueryable<Campaign> WithStores() => db.Campaigns.AsNoTracking().Include(campaign => campaign.Stores).ThenInclude(link => link.Store).Include(campaign => campaign.Products);

    public async Task<IReadOnlyList<Product>> GetActiveProductsAsync(IReadOnlyCollection<Guid> ids, CancellationToken cancellationToken = default) =>
        await db.Products.AsNoTracking().Where(item => ids.Contains(item.Id) && item.IsActive).ToListAsync(cancellationToken);

    public async Task<IReadOnlyList<Product>> SearchActiveProductsAsync(string? search, int take, CancellationToken cancellationToken = default) =>
        await db.Products.AsNoTracking().Where(item => item.IsActive && (search == null || item.Name.Contains(search))).OrderBy(item => item.Name).Take(take).ToListAsync(cancellationToken);

    public async Task<IReadOnlyDictionary<Guid, string>> GetProductNamesAsync(IReadOnlyCollection<Guid> ids, CancellationToken cancellationToken = default) =>
        await db.Products.AsNoTracking().Where(item => ids.Contains(item.Id)).ToDictionaryAsync(item => item.Id, item => item.Name, cancellationToken);

    public async Task<Campaign?> TryEndDiscountAsync(Guid id, DateTime now, CancellationToken cancellationToken = default)
    {
        // Only a discount that is on can be ended; ending it twice, or after it ran out, changes nothing.
        await db.Campaigns.Where(campaign => campaign.Id == id && campaign.IsActive && campaign.DiscountPercent > 0 && campaign.EndsAt > now)
            .ExecuteUpdateAsync(set => set.SetProperty(campaign => campaign.EndsAt, now), cancellationToken);
        db.ChangeTracker.Clear();
        return await GetCampaignAsync(id, cancellationToken);
    }

    public Task<IReadOnlyDictionary<Guid, int>> GetPercentsAsync(Guid storeId, IReadOnlyCollection<Guid> productIds, DateTime now, CancellationToken cancellationToken = default) =>
        OfferDiscounts.PercentsAsync(db, storeId, productIds, now, cancellationToken);

    public async Task<OfferDetail?> GetOfferAsync(Guid id, DateTime now, CancellationToken cancellationToken = default)
    {
        var campaign = await GetCampaignAsync(id, cancellationToken);
        return campaign is not null && OfferRules.IsOpenable(campaign, now) ? OfferRules.Detail(campaign, now) : null;
    }

    public async Task<IReadOnlyList<ActiveOffer>> GetActiveOffersAsync(Guid storeId, DateTime now, CancellationToken cancellationToken = default)
    {
        var earliestMessage = now.AddDays(-OfferRules.MessageBannerDays);
        var candidates = await WithStores()
            .Where(campaign => campaign.IsActive && campaign.StartsAt <= now
                && (campaign.PaymentStatus == PaymentState.NotRequired || campaign.PaymentStatus == PaymentState.Paid)
                && (!campaign.Stores.Any() || campaign.Stores.Any(link => link.StoreId == storeId))
                && ((campaign.DiscountPercent > 0 && campaign.EndsAt > now) || (campaign.DiscountPercent == 0 && campaign.StartsAt > earliestMessage)))
            .OrderByDescending(campaign => campaign.StartsAt)
            .Take(20)
            .ToListAsync(cancellationToken);
        return candidates
            .Where(campaign => OfferRules.ShowsAsBanner(campaign, now))
            .Select(campaign => new ActiveOffer(campaign.Id, campaign.TitleEn, string.IsNullOrEmpty(campaign.BodyEn) ? null : campaign.BodyEn, campaign.TitleMr, campaign.BodyMr, campaign.ImageUrl, campaign.DiscountPercent, campaign.EndsAt, campaign.Products.Select(link => link.ProductId).ToArray(), campaign.ShowOnLoad))
            .ToArray();
    }

    public async Task<IReadOnlyList<Campaign>> ListCampaignsAsync(CancellationToken cancellationToken = default) =>
        await WithStores().OrderByDescending(campaign => campaign.StartsAt).ToListAsync(cancellationToken);

    public async Task AddCampaignAsync(Campaign campaign, CancellationToken cancellationToken = default)
    {
        db.Campaigns.Add(campaign);
        await db.SaveChangesAsync(cancellationToken);
        db.ChangeTracker.Clear();
    }

    public Task<Campaign?> GetCampaignAsync(Guid id, CancellationToken cancellationToken = default) =>
        WithStores().FirstOrDefaultAsync(campaign => campaign.Id == id, cancellationToken);

    public async Task<IReadOnlyList<Store>> GetStoresAsync(IReadOnlyCollection<Guid> ids, Guid organizationId, CancellationToken cancellationToken = default) =>
        await db.Stores.AsNoTracking().Where(item => ids.Contains(item.Id) && item.OrganizationId == organizationId && item.IsActive).ToListAsync(cancellationToken);

    public async Task<Campaign?> TryCancelCampaignAsync(Guid id, CancellationToken cancellationToken = default)
    {
        // Only a campaign nobody has sent yet can be cancelled; if the sender wins the race, the answer below says it was sent.
        // A fee already paid is queued for a refund in the same step, an unpaid one is closed.
        for (var attempt = 1; ; attempt++)
        {
            try
            {
                await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
                var cancelled = await db.Campaigns.Where(campaign => campaign.Id == id && campaign.PublishedAt == null && campaign.IsActive)
                    .ExecuteUpdateAsync(set => set.SetProperty(campaign => campaign.IsActive, false), cancellationToken);
                if (cancelled > 0)
                {
                    var tracked = await db.Campaigns.SingleAsync(campaign => campaign.Id == id, cancellationToken);
                    var payment = await db.CampaignPayments.FirstOrDefaultAsync(item => item.CampaignId == id, cancellationToken);
                    CampaignPaymentTransitions.ApplyCancelled(tracked, payment, DateTime.UtcNow);
                    await db.SaveChangesAsync(cancellationToken);
                }

                await transaction.CommitAsync(cancellationToken);
                break;
            }
            catch (Exception ex) when ((ex is DbUpdateConcurrencyException || SqlErrors.IsDeadlock(ex)) && attempt < 5)
            {
                // A payment landed at the same moment (or the database chose this request as a deadlock victim): look again on fresh data.
                db.ChangeTracker.Clear();
            }
        }

        db.ChangeTracker.Clear();
        return await GetCampaignAsync(id, cancellationToken);
    }

    public async Task<int> PublishDueCampaignsAsync(DateTime now, CancellationToken cancellationToken = default)
    {
        var due = await db.Campaigns.AsNoTracking()
            .Where(campaign => campaign.IsActive && campaign.PublishedAt == null && campaign.StartsAt <= now && (campaign.PaymentStatus == PaymentState.NotRequired || campaign.PaymentStatus == PaymentState.Paid))
            .OrderBy(campaign => campaign.StartsAt)
            .Select(campaign => campaign.Id)
            .Take(20)
            .ToListAsync(cancellationToken);
        var sent = 0;
        foreach (var id in due)
        {
            await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
            var claimed = await db.Campaigns
                .Where(campaign => campaign.Id == id && campaign.IsActive && campaign.PublishedAt == null && campaign.StartsAt <= now && (campaign.PaymentStatus == PaymentState.NotRequired || campaign.PaymentStatus == PaymentState.Paid))
                .ExecuteUpdateAsync(set => set.SetProperty(campaign => campaign.PublishedAt, now), cancellationToken);
            if (claimed == 0)
            {
                await transaction.RollbackAsync(cancellationToken);
                continue;
            }

            var campaign = await db.Campaigns.AsNoTracking().SingleAsync(item => item.Id == id, cancellationToken);
            if (!campaign.Notify)
            {
                // A quiet discount: nothing to send, but it is marked sent so the sender never looks at it again.
                await transaction.CommitAsync(cancellationToken);
                sent++;
                continue;
            }

            // One notification for every customer who has offers on and is a customer of the campaign's stores (or every customer when the
            // campaign names no store). "A customer of a store" = has ordered from it or has a cart there. The unique (campaign, customer)
            // index guards against a second send. The notification carries the name of one store the customer knows from the campaign.
            var recipients = await db.Database.ExecuteSqlInterpolatedAsync($"""
                INSERT INTO Notifications (Id, CustomerId, OrderId, Type, Title, Message, IsRead, CreatedAt, Category, DataJson, CampaignId)
                SELECT NEWID(), c.Id, NULL, {NotificationTypes.Offer}, {campaign.TitleEn}, {campaign.BodyEn}, 0, {now}, {NotificationCategories.Offer},
                    (SELECT TOP 1 CONCAT(CHAR(123), '"store":"', REPLACE(REPLACE(s.Name, '\', '\\'), '"', '\"'), '"', CHAR(125))
                     FROM CampaignStores cs JOIN Stores s ON s.Id = cs.StoreId
                     WHERE cs.CampaignId = {id}
                       AND (EXISTS (SELECT 1 FROM Orders o WHERE o.UserId = c.UserId AND o.StoreId = cs.StoreId)
                            OR EXISTS (SELECT 1 FROM Carts ca WHERE ca.CustomerId = c.Id AND ca.StoreId = cs.StoreId))
                     ORDER BY s.Name),
                    {id}
                FROM Customers c
                WHERE c.IsActive = 1 AND c.OffersEnabled = 1
                  AND NOT EXISTS (SELECT 1 FROM Notifications n WHERE n.CampaignId = {id} AND n.CustomerId = c.Id)
                  AND (NOT EXISTS (SELECT 1 FROM CampaignStores cs WHERE cs.CampaignId = {id})
                       OR EXISTS (SELECT 1 FROM CampaignStores cs
                                  WHERE cs.CampaignId = {id}
                                    AND (EXISTS (SELECT 1 FROM Orders o WHERE o.UserId = c.UserId AND o.StoreId = cs.StoreId)
                                         OR EXISTS (SELECT 1 FROM Carts ca WHERE ca.CustomerId = c.Id AND ca.StoreId = cs.StoreId))))
                """, cancellationToken);
            await db.Campaigns.Where(item => item.Id == id).ExecuteUpdateAsync(set => set.SetProperty(item => item.RecipientCount, recipients), cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            sent++;
        }

        return sent;
    }
}
