using Microsoft.EntityFrameworkCore;
using QuickCommerce.Application.Interfaces;
using QuickCommerce.Application.Services;
using QuickCommerce.Domain;

namespace QuickCommerce.Infrastructure.Persistence;

/// <summary>
/// Paid placement on SQL Server. A bid and the bid it beats change in one transaction, in two steps so two bids never lead one pincode and
/// week at once (the database also refuses it with a unique index). Two things reaching the same bid at the same moment are told apart by
/// the row version, and a deadlock victim simply asks again.
/// </summary>
public sealed class EfPlacementStore(QuickCommerceDbContext db) : IPlacementStore
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

    public async Task<PlacementRules> GetRulesAsync(CancellationToken cancellationToken = default) =>
        await db.PlacementRules.AsNoTracking().FirstOrDefaultAsync(cancellationToken) ?? new PlacementRules();

    public async Task SetRulesAsync(PlacementRules rules, CancellationToken cancellationToken = default)
    {
        var existing = await db.PlacementRules.FirstOrDefaultAsync(cancellationToken);
        if (existing is null)
        {
            db.PlacementRules.Add(rules);
        }
        else
        {
            existing.Enabled = rules.Enabled;
            existing.MinBidPaise = rules.MinBidPaise;
            existing.MinStepPaise = rules.MinStepPaise;
            existing.MaxWeeksAhead = rules.MaxWeeksAhead;
            existing.UpdatedAt = rules.UpdatedAt;
            existing.UpdatedBy = rules.UpdatedBy;
        }

        await db.SaveChangesAsync(cancellationToken);
        db.ChangeTracker.Clear();
    }

    public Task<Store?> GetStoreAsync(Guid storeId, Guid organizationId, CancellationToken cancellationToken = default) =>
        db.Stores.AsNoTracking().FirstOrDefaultAsync(store => store.Id == storeId && store.OrganizationId == organizationId && store.IsActive, cancellationToken);

    public Task<PlacementBid?> GetLeadingAsync(string pincode, DateOnly weekStart, CancellationToken cancellationToken = default) =>
        db.PlacementBids.AsNoTracking().Include(bid => bid.Store).FirstOrDefaultAsync(bid => bid.Pincode == pincode && bid.WeekStart == weekStart && bid.Status == PlacementBidStatus.Leading, cancellationToken);

    public async Task<Guid?> GetLeadingStoreAsync(string pincode, DateTime nowUtc, CancellationToken cancellationToken = default)
    {
        if (!PlacementWeeks.IsPincode(pincode) || !(await GetRulesAsync(cancellationToken)).Enabled)
        {
            return null;
        }

        var week = PlacementWeeks.WeekStartOf(nowUtc);
        return await db.PlacementBids.AsNoTracking()
            .Where(bid => bid.Pincode == pincode && bid.WeekStart == week && bid.Status == PlacementBidStatus.Leading)
            .Select(bid => (Guid?)bid.StoreId)
            .FirstOrDefaultAsync(cancellationToken);
    }

    public Task<PlacementBid?> GetBidAsync(Guid bidId, CancellationToken cancellationToken = default) =>
        db.PlacementBids.AsNoTracking().Include(bid => bid.Store).FirstOrDefaultAsync(bid => bid.Id == bidId, cancellationToken);

    public async Task<IReadOnlyList<PlacementBid>> ListBidsAsync(Guid? storeId, int max, CancellationToken cancellationToken = default) =>
        await db.PlacementBids.AsNoTracking().Include(bid => bid.Store)
            .Where(bid => storeId == null || bid.StoreId == storeId)
            .OrderByDescending(bid => bid.CreatedAt)
            .Take(max)
            .ToListAsync(cancellationToken);

    public Task<int> CountOpenBidsAsync(Guid storeId, CancellationToken cancellationToken = default) =>
        db.PlacementBids.CountAsync(bid => bid.StoreId == storeId && bid.Status == PlacementBidStatus.AwaitingPayment, cancellationToken);

    public async Task AddBidAsync(PlacementBid bid, CancellationToken cancellationToken = default)
    {
        db.PlacementBids.Add(bid);
        await db.SaveChangesAsync(cancellationToken);
        db.ChangeTracker.Clear();
    }

    public Task<PlacementBid?> CancelBidAsync(Guid bidId, DateTime now, CancellationToken cancellationToken = default) => RetryAsync(async () =>
    {
        var bid = await db.PlacementBids.FirstOrDefaultAsync(item => item.Id == bidId, cancellationToken);
        if (bid is null)
        {
            return null;
        }

        PlacementTransitions.ApplyCancelled(bid, now);
        await db.SaveChangesAsync(cancellationToken);
        db.ChangeTracker.Clear();
        return await GetBidAsync(bidId, cancellationToken);
    });

    public Task<PlacementBid?> BeginAttemptAsync(Guid bidId, DateTime now, CancellationToken cancellationToken = default) => RetryAsync<PlacementBid?>(async () =>
    {
        var bid = await db.PlacementBids.FirstOrDefaultAsync(item => item.Id == bidId, cancellationToken);
        if (bid is null || bid.Status != PlacementBidStatus.AwaitingPayment)
        {
            return null;
        }

        bid.Attempts++;
        bid.UpdatedAt = now;
        await db.SaveChangesAsync(cancellationToken);
        db.ChangeTracker.Clear();
        return bid;
    });

    public async Task<bool> AttachProviderOrderAsync(Guid bidId, string providerOrderId, CancellationToken cancellationToken = default) =>
        await db.PlacementBids.Where(bid => bid.Id == bidId && bid.ProviderOrderId == null)
            .ExecuteUpdateAsync(set => set.SetProperty(bid => bid.ProviderOrderId, providerOrderId), cancellationToken) > 0;

    public Task<PlacementCaptureOutcome> ApplyCapturedAsync(string providerOrderId, string providerPaymentId, long paidPaise, DateTime now, CancellationToken cancellationToken = default) => RetryAsync(async () =>
    {
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        var bid = await db.PlacementBids.FirstOrDefaultAsync(item => item.ProviderOrderId == providerOrderId, cancellationToken);
        if (bid is null)
        {
            return PlacementCaptureOutcome.UnknownPayment;
        }

        var current = PlacementTransitions.Settled(bid)
            ? null
            : await db.PlacementBids.FirstOrDefaultAsync(item => item.Pincode == bid.Pincode && item.WeekStart == bid.WeekStart && item.Status == PlacementBidStatus.Leading && item.Id != bid.Id, cancellationToken);
        var outcome = PlacementTransitions.ApplyCaptured(bid, current, providerPaymentId, paidPaise, now);
        if (outcome == PlacementCaptureOutcome.Leading && current is not null)
        {
            // The beaten bid stops leading first, so two bids never lead at once.
            var intended = bid.Status;
            bid.Status = PlacementBidStatus.AwaitingPayment;
            await db.SaveChangesAsync(cancellationToken);
            bid.Status = intended;
            await db.SaveChangesAsync(cancellationToken);
        }
        else if (outcome != PlacementCaptureOutcome.AlreadyPaid)
        {
            await db.SaveChangesAsync(cancellationToken);
        }

        await transaction.CommitAsync(cancellationToken);
        db.ChangeTracker.Clear();
        return outcome;
    });

    public async Task<IReadOnlyList<PlacementBid>> GetRefundsToStartAsync(int max, CancellationToken cancellationToken = default) => await db.PlacementBids.AsNoTracking()
        .Where(bid => bid.PaymentStatus == PaymentState.Refunding && bid.RefundId == null)
        .OrderBy(bid => bid.UpdatedAt)
        .Take(max)
        .ToListAsync(cancellationToken);

    public Task SetRefundStartedAsync(Guid bidId, string refundId, bool processed, DateTime now, CancellationToken cancellationToken = default) => RetryAsync(async () =>
    {
        var bid = await db.PlacementBids.SingleAsync(item => item.Id == bidId, cancellationToken);
        bid.RefundId = refundId;
        bid.UpdatedAt = now;
        if (processed)
        {
            bid.PaymentStatus = PaymentState.Refunded;
        }

        await db.SaveChangesAsync(cancellationToken);
        db.ChangeTracker.Clear();
        return true;
    });

    public Task SetRefundFailedAsync(Guid bidId, string reason, bool permanent, DateTime now, CancellationToken cancellationToken = default) => RetryAsync(async () =>
    {
        var bid = await db.PlacementBids.SingleAsync(item => item.Id == bidId, cancellationToken);
        bid.FailureReason = reason.Length <= 200 ? reason : reason[..200];
        bid.UpdatedAt = now;
        if (permanent)
        {
            bid.PaymentStatus = PaymentState.RefundFailed;
        }

        await db.SaveChangesAsync(cancellationToken);
        db.ChangeTracker.Clear();
        return true;
    });

    public Task SetRefundedAsync(Guid bidId, DateTime now, CancellationToken cancellationToken = default) => RetryAsync(async () =>
    {
        var bid = await db.PlacementBids.SingleAsync(item => item.Id == bidId, cancellationToken);
        bid.PaymentStatus = PaymentState.Refunded;
        bid.UpdatedAt = now;
        await db.SaveChangesAsync(cancellationToken);
        db.ChangeTracker.Clear();
        return true;
    });

    public async Task<IReadOnlyList<PlacementBid>> GetUnsettledAsync(DateTime olderThan, int max, CancellationToken cancellationToken = default)
    {
        var weekAgo = olderThan.AddDays(-7);
        return await db.PlacementBids.AsNoTracking()
            .Where(bid => bid.UpdatedAt < olderThan && bid.CreatedAt > weekAgo &&
                ((bid.Status == PlacementBidStatus.AwaitingPayment && bid.ProviderOrderId != null) || (bid.PaymentStatus == PaymentState.Refunding && bid.RefundId != null)))
            .OrderBy(bid => bid.UpdatedAt)
            .Take(max)
            .ToListAsync(cancellationToken);
    }
}
