using Microsoft.EntityFrameworkCore;
using QuickCommerce.Application.Interfaces;
using QuickCommerce.Application.Services;
using QuickCommerce.Domain;

namespace QuickCommerce.Infrastructure.Persistence;

/// <summary>The fee of an offer on SQL Server: default and per-store amounts.</summary>
public sealed class EfOfferFeeStore(QuickCommerceDbContext db) : IOfferFeeStore
{
    public async Task<int> GetDefaultAsync(CancellationToken cancellationToken = default) =>
        await db.OfferFees.AsNoTracking().Where(fee => fee.StoreId == null).Select(fee => fee.AmountPaise).FirstOrDefaultAsync(cancellationToken);

    public async Task<int> GetLoadFeeAsync(CancellationToken cancellationToken = default) =>
        await db.OfferFees.AsNoTracking().Where(fee => fee.StoreId == null).Select(fee => fee.LoadAmountPaise).FirstOrDefaultAsync(cancellationToken);

    public async Task SetLoadFeeAsync(int amountPaise, string? by, DateTime now, CancellationToken cancellationToken = default)
    {
        for (var attempt = 0; attempt < 3; attempt++)
        {
            var updated = await db.OfferFees.Where(fee => fee.StoreId == null)
                .ExecuteUpdateAsync(set => set.SetProperty(fee => fee.LoadAmountPaise, amountPaise).SetProperty(fee => fee.UpdatedAt, now).SetProperty(fee => fee.UpdatedBy, by), cancellationToken);
            if (updated > 0)
            {
                return;
            }

            try
            {
                db.OfferFees.Add(new OfferFee { StoreId = null, AmountPaise = 0, LoadAmountPaise = amountPaise, UpdatedAt = now, UpdatedBy = by });
                await db.SaveChangesAsync(cancellationToken);
                db.ChangeTracker.Clear();
                return;
            }
            catch (DbUpdateException)
            {
                db.ChangeTracker.Clear();
            }
        }
    }

    public async Task<IReadOnlyDictionary<Guid, int>> GetOverridesAsync(CancellationToken cancellationToken = default) =>
        await db.OfferFees.AsNoTracking().Where(fee => fee.StoreId != null).ToDictionaryAsync(fee => fee.StoreId!.Value, fee => fee.AmountPaise, cancellationToken);

    public async Task SetAsync(Guid? storeId, int? amountPaise, string? by, DateTime now, CancellationToken cancellationToken = default)
    {
        if (amountPaise is null)
        {
            // No amount for a store means "use the default": its own row goes away.
            await db.OfferFees.Where(fee => fee.StoreId == storeId).ExecuteDeleteAsync(cancellationToken);
            return;
        }

        for (var attempt = 0; attempt < 3; attempt++)
        {
            var updated = await db.OfferFees.Where(fee => fee.StoreId == storeId)
                .ExecuteUpdateAsync(set => set.SetProperty(fee => fee.AmountPaise, amountPaise.Value).SetProperty(fee => fee.UpdatedAt, now).SetProperty(fee => fee.UpdatedBy, by), cancellationToken);
            if (updated > 0)
            {
                return;
            }

            try
            {
                db.OfferFees.Add(new OfferFee { StoreId = storeId, AmountPaise = amountPaise.Value, UpdatedAt = now, UpdatedBy = by });
                await db.SaveChangesAsync(cancellationToken);
                db.ChangeTracker.Clear();
                return;
            }
            catch (DbUpdateException)
            {
                // Someone set it at the same moment: the next round updates their row.
                db.ChangeTracker.Clear();
            }
        }
    }

    public async Task<IReadOnlyList<Store>> GetStoresAsync(Guid organizationId, CancellationToken cancellationToken = default) =>
        await db.Stores.AsNoTracking().Where(store => store.OrganizationId == organizationId && store.IsActive).ToListAsync(cancellationToken);
}

/// <summary>
/// Offer fee payments on SQL Server. Each change to an offer and its payment is one SaveChanges, so it all happens or none of it does; two
/// things reaching the same payment at once (confirming while the lookup finds it, or cancelling while it lands) are told apart by the row version.
/// </summary>
public sealed class EfCampaignPaymentStore(QuickCommerceDbContext db) : ICampaignPaymentStore
{
    private const int Attempts = 5;

    private async Task<T> RetryAsync<T>(Func<Task<T>> work)
    {
        for (var attempt = 1; ; attempt++)
        {
            try
            {
                return await work();
            }
            catch (Exception ex) when ((ex is DbUpdateConcurrencyException || ex is DbUpdateException || SqlErrors.IsDeadlock(ex)) && attempt < Attempts)
            {
                db.ChangeTracker.Clear();
            }
        }
    }

    public Task<CampaignPayment?> GetPaymentAsync(Guid campaignId, CancellationToken cancellationToken = default) =>
        db.CampaignPayments.AsNoTracking().FirstOrDefaultAsync(payment => payment.CampaignId == campaignId, cancellationToken);

    public Task<CampaignPayment?> BeginAttemptAsync(Guid campaignId, DateTime now, CancellationToken cancellationToken = default) => RetryAsync<CampaignPayment?>(async () =>
    {
        var campaign = await db.Campaigns.FirstOrDefaultAsync(item => item.Id == campaignId, cancellationToken);
        if (campaign is null || !CampaignMapping.AwaitsPayment(campaign))
        {
            return null;
        }

        var payment = await db.CampaignPayments.FirstOrDefaultAsync(item => item.CampaignId == campaignId, cancellationToken);
        if (payment is null)
        {
            payment = new CampaignPayment { CampaignId = campaignId, AmountPaise = campaign.FeePaise, CreatedAt = now, UpdatedAt = now };
            db.CampaignPayments.Add(payment);
        }

        payment.Attempts++;
        payment.UpdatedAt = now;
        if (payment.Status == PaymentState.Failed)
        {
            payment.Status = PaymentState.Created;
            campaign.PaymentStatus = PaymentState.Created;
        }

        await db.SaveChangesAsync(cancellationToken);
        db.ChangeTracker.Clear();
        return payment;
    });

    public async Task<bool> AttachProviderOrderAsync(Guid paymentId, string providerOrderId, CancellationToken cancellationToken = default) =>
        await db.CampaignPayments.Where(payment => payment.Id == paymentId && payment.ProviderOrderId == null)
            .ExecuteUpdateAsync(set => set.SetProperty(payment => payment.ProviderOrderId, providerOrderId), cancellationToken) > 0;

    public Task<CapturedOutcome> ApplyCapturedAsync(string providerOrderId, string providerPaymentId, long paidPaise, DateTime now, CancellationToken cancellationToken = default) => RetryAsync(async () =>
    {
        var payment = await db.CampaignPayments.FirstOrDefaultAsync(item => item.ProviderOrderId == providerOrderId, cancellationToken);
        if (payment is null)
        {
            return CapturedOutcome.UnknownPayment;
        }

        var campaign = await db.Campaigns.SingleAsync(item => item.Id == payment.CampaignId, cancellationToken);
        var outcome = CampaignPaymentTransitions.ApplyCaptured(campaign, payment, providerPaymentId, paidPaise, now);
        if (outcome != CapturedOutcome.AlreadyPaid)
        {
            await db.SaveChangesAsync(cancellationToken);
        }

        db.ChangeTracker.Clear();
        return outcome;
    });

    public async Task<IReadOnlyList<CampaignPayment>> GetRefundsToStartAsync(int max, CancellationToken cancellationToken = default) => await db.CampaignPayments.AsNoTracking()
        .Where(payment => payment.Status == PaymentState.Refunding && payment.RefundId == null)
        .OrderBy(payment => payment.UpdatedAt)
        .Take(max)
        .ToListAsync(cancellationToken);

    public Task SetRefundStartedAsync(Guid paymentId, string refundId, bool processed, DateTime now, CancellationToken cancellationToken = default) => RetryAsync(async () =>
    {
        var payment = await db.CampaignPayments.SingleAsync(item => item.Id == paymentId, cancellationToken);
        var campaign = await db.Campaigns.SingleAsync(item => item.Id == payment.CampaignId, cancellationToken);
        payment.RefundId = refundId;
        payment.UpdatedAt = now;
        if (processed)
        {
            CampaignPaymentTransitions.ApplyRefunded(campaign, payment, now);
        }

        await db.SaveChangesAsync(cancellationToken);
        db.ChangeTracker.Clear();
        return true;
    });

    public Task SetRefundFailedAsync(Guid paymentId, string reason, bool permanent, DateTime now, CancellationToken cancellationToken = default) => RetryAsync(async () =>
    {
        var payment = await db.CampaignPayments.SingleAsync(item => item.Id == paymentId, cancellationToken);
        if (permanent)
        {
            var campaign = await db.Campaigns.SingleAsync(item => item.Id == payment.CampaignId, cancellationToken);
            CampaignPaymentTransitions.ApplyRefundFailed(campaign, payment, reason, now);
        }
        else
        {
            payment.FailureReason = reason.Length <= 200 ? reason : reason[..200];
            payment.UpdatedAt = now;
        }

        await db.SaveChangesAsync(cancellationToken);
        db.ChangeTracker.Clear();
        return true;
    });

    public Task SetRefundedAsync(Guid paymentId, DateTime now, CancellationToken cancellationToken = default) => RetryAsync(async () =>
    {
        var payment = await db.CampaignPayments.SingleAsync(item => item.Id == paymentId, cancellationToken);
        var campaign = await db.Campaigns.SingleAsync(item => item.Id == payment.CampaignId, cancellationToken);
        CampaignPaymentTransitions.ApplyRefunded(campaign, payment, now);
        await db.SaveChangesAsync(cancellationToken);
        db.ChangeTracker.Clear();
        return true;
    });

    public async Task<IReadOnlyList<CampaignPayment>> GetUnsettledAsync(DateTime olderThan, int max, CancellationToken cancellationToken = default)
    {
        var weekAgo = olderThan.AddDays(-7);
        return await db.CampaignPayments.AsNoTracking()
            .Where(payment => payment.UpdatedAt < olderThan && payment.CreatedAt > weekAgo &&
                (((payment.Status == PaymentState.Created || payment.Status == PaymentState.Failed) && payment.ProviderOrderId != null) ||
                 (payment.Status == PaymentState.Refunding && payment.RefundId != null)))
            .OrderBy(payment => payment.UpdatedAt)
            .Take(max)
            .ToListAsync(cancellationToken);
    }
}
