using Microsoft.EntityFrameworkCore;
using QuickCommerce.Application.Interfaces;
using QuickCommerce.Domain;

namespace QuickCommerce.Infrastructure.Persistence;

/// <summary>Campaigns on SQL Server. Sending is claimed with one conditional update, so only one server sends a campaign, and each customer gets it once.</summary>
public sealed class EfCampaignStore(QuickCommerceDbContext db) : ICampaignStore
{
    public async Task<IReadOnlyList<Campaign>> ListCampaignsAsync(CancellationToken cancellationToken = default) =>
        await db.Campaigns.AsNoTracking().OrderByDescending(campaign => campaign.StartsAt).ToListAsync(cancellationToken);

    public async Task AddCampaignAsync(Campaign campaign, CancellationToken cancellationToken = default)
    {
        db.Campaigns.Add(campaign);
        await db.SaveChangesAsync(cancellationToken);
        db.ChangeTracker.Clear();
    }

    public Task<Campaign?> GetCampaignAsync(Guid id, CancellationToken cancellationToken = default) =>
        db.Campaigns.AsNoTracking().FirstOrDefaultAsync(campaign => campaign.Id == id, cancellationToken);

    public async Task<Campaign?> TryCancelCampaignAsync(Guid id, CancellationToken cancellationToken = default)
    {
        // Only a campaign nobody has sent yet can be cancelled; if the sender wins the race, the answer below says it was sent.
        await db.Campaigns.Where(campaign => campaign.Id == id && campaign.PublishedAt == null && campaign.IsActive)
            .ExecuteUpdateAsync(set => set.SetProperty(campaign => campaign.IsActive, false), cancellationToken);
        return await GetCampaignAsync(id, cancellationToken);
    }

    public async Task<int> PublishDueCampaignsAsync(DateTime now, CancellationToken cancellationToken = default)
    {
        var due = await db.Campaigns.AsNoTracking()
            .Where(campaign => campaign.IsActive && campaign.PublishedAt == null && campaign.StartsAt <= now)
            .OrderBy(campaign => campaign.StartsAt)
            .Select(campaign => campaign.Id)
            .Take(20)
            .ToListAsync(cancellationToken);
        var sent = 0;
        foreach (var id in due)
        {
            await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
            var claimed = await db.Campaigns
                .Where(campaign => campaign.Id == id && campaign.IsActive && campaign.PublishedAt == null && campaign.StartsAt <= now)
                .ExecuteUpdateAsync(set => set.SetProperty(campaign => campaign.PublishedAt, now), cancellationToken);
            if (claimed == 0)
            {
                await transaction.RollbackAsync(cancellationToken);
                continue;
            }

            var campaign = await db.Campaigns.AsNoTracking().SingleAsync(item => item.Id == id, cancellationToken);
            // One notification for every customer who has offers on. The unique (campaign, customer) index guards against a second send.
            var recipients = await db.Database.ExecuteSqlInterpolatedAsync($"""
                INSERT INTO Notifications (Id, CustomerId, OrderId, Type, Title, Message, IsRead, CreatedAt, Category, DataJson, CampaignId)
                SELECT NEWID(), c.Id, NULL, {NotificationTypes.Offer}, {campaign.TitleEn}, {campaign.BodyEn}, 0, {now}, {NotificationCategories.Offer}, NULL, {id}
                FROM Customers c
                WHERE c.IsActive = 1 AND c.OffersEnabled = 1
                  AND NOT EXISTS (SELECT 1 FROM Notifications n WHERE n.CampaignId = {id} AND n.CustomerId = c.Id)
                """, cancellationToken);
            await db.Campaigns.Where(item => item.Id == id).ExecuteUpdateAsync(set => set.SetProperty(item => item.RecipientCount, recipients), cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            sent++;
        }

        return sent;
    }
}
