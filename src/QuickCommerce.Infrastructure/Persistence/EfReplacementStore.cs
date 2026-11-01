using Microsoft.EntityFrameworkCore;
using QuickCommerce.Application.Interfaces;
using QuickCommerce.Application.Services;
using QuickCommerce.Domain;

namespace QuickCommerce.Infrastructure.Persistence;

/// <summary>Replacement proposals on SQL Server. Every answer changes the order, its payment, the stock and the proposal in one save.</summary>
public sealed class EfReplacementStore(QuickCommerceDbContext db) : IReplacementStore
{
    public async Task<ReplacementCommit> ProposeAsync(Guid orderId, Guid organizationId, Guid? storeId, Guid proposedBy, ProposeReplacementRequest request, DateTime now, TimeSpan window, CancellationToken cancellationToken = default)
    {
        var order = await db.Orders.AsNoTracking().Include(current => current.Items)
            .SingleOrDefaultAsync(current => current.Id == orderId && (!storeId.HasValue || current.StoreId == storeId.Value) &&
                db.Stores.Any(store => store.Id == current.StoreId && store.OrganizationId == organizationId), cancellationToken);
        if (order is null)
        {
            return new(ReplacementOutcome.NotFound, "Order not found.");
        }

        // Anything left open past its time is closed first, so a stale proposal never blocks a new one.
        await ExpireOrderAsync(order.Id, now, cancellationToken);
        if (await db.ReplacementProposals.AnyAsync(item => item.OrderId == orderId && item.Status == "Pending", cancellationToken))
        {
            return new(ReplacementOutcome.Conflict, "A replacement is already waiting for the customer's answer.", ReplacementReasons.AlreadyOpen);
        }

        var payment = await db.Payments.AsNoTracking().SingleOrDefaultAsync(item => item.OrderId == orderId, cancellationToken);
        var product = await db.Products.AsNoTracking().SingleOrDefaultAsync(item => item.Id == request.ReplacementProductId, cancellationToken);
        var variant = await db.ProductVariants.AsNoTracking().SingleOrDefaultAsync(item => item.Id == request.ReplacementVariantId, cancellationToken);
        var stock = await db.StoreVariantInventory.AsNoTracking().SingleOrDefaultAsync(item => item.StoreId == order.StoreId && item.VariantId == request.ReplacementVariantId, cancellationToken);
        var percents = await OfferDiscounts.PercentsAsync(db, order.StoreId, [request.ReplacementProductId], now, cancellationToken);
        var commit = ReplacementRules.Propose(order, payment, request, product, variant, stock, percents.GetValueOrDefault(request.ReplacementProductId), proposedBy, now, window);
        if (commit.Outcome != ReplacementOutcome.Applied)
        {
            return commit;
        }

        db.ReplacementProposals.Add(commit.Proposal!);
        var customerId = await db.Customers.Where(customer => customer.UserId == order.UserId).Select(customer => (Guid?)customer.Id).SingleOrDefaultAsync(cancellationToken);
        if (customerId.HasValue)
        {
            db.Notifications.Add(NotificationCatalog.Create(customerId.Value, order.Id, NotificationTypes.ReplacementProposed, order.OrderNumber));
        }

        try
        {
            await db.SaveChangesAsync(cancellationToken);
            return commit;
        }
        catch (DbUpdateException)
        {
            // Two proposals at once: the unique index let only one through.
            db.ChangeTracker.Clear();
            return new(ReplacementOutcome.Conflict, "A replacement is already waiting for the customer's answer.", ReplacementReasons.AlreadyOpen);
        }
    }

    public async Task<IReadOnlyList<ReplacementProposal>> ListAsync(Guid orderId, CancellationToken cancellationToken = default) =>
        await db.ReplacementProposals.AsNoTracking().Where(item => item.OrderId == orderId).OrderBy(item => item.CreatedAt).ToListAsync(cancellationToken);

    public async Task<ReplacementCommit> AnswerAsync(Guid orderId, Guid proposalId, Guid userId, bool accept, DateTime now, CancellationToken cancellationToken = default)
    {
        var order = await db.Orders.Include(current => current.Items).SingleOrDefaultAsync(current => current.Id == orderId && current.UserId == userId, cancellationToken);
        var proposal = order is null ? null : await db.ReplacementProposals.SingleOrDefaultAsync(item => item.Id == proposalId && item.OrderId == orderId, cancellationToken);
        if (order is null || proposal is null)
        {
            return new(ReplacementOutcome.NotFound, "Replacement not found.");
        }

        if (proposal.Status != "Pending")
        {
            return new(ReplacementOutcome.WrongState, "That replacement has already been answered.");
        }

        var payment = await db.Payments.SingleOrDefaultAsync(item => item.OrderId == orderId, cancellationToken);
        ReplacementCommit commit;
        if (accept && now <= proposal.ExpiresAt)
        {
            var stock = await db.StoreVariantInventory.SingleOrDefaultAsync(item => item.StoreId == order.StoreId && item.VariantId == proposal.NewVariantId, cancellationToken);
            commit = ReplacementRules.Accept(order, payment, proposal, stock, userId, now);
            if (commit.Outcome == ReplacementOutcome.Applied)
            {
                db.OrderAdjustments.Add(commit.Adjustment!);
            }
            else if (commit.Reason == ReplacementReasons.OutOfStock)
            {
                // The replacement ran out while the customer was deciding: the item goes, as if declined.
                ApplyRemoval(order, payment, proposal, userId, now, "Declined");
                commit = new(ReplacementOutcome.Conflict, commit.Message, commit.Reason);
            }
        }
        else
        {
            var late = accept;
            ApplyRemoval(order, payment, proposal, userId, now, late ? "Expired" : "Declined");
            commit = late
                ? new(ReplacementOutcome.Conflict, "That replacement has run out of time. The item was removed and any payment for it refunded.", ReplacementReasons.Expired, proposal)
                : new(ReplacementOutcome.Applied, Proposal: proposal);
        }

        return await SaveAsync(commit, order, cancellationToken);
    }

    public async Task<int> ExpireDueAsync(DateTime now, int max, CancellationToken cancellationToken = default)
    {
        var due = await db.ReplacementProposals.AsNoTracking().Where(item => item.Status == "Pending" && item.ExpiresAt < now).OrderBy(item => item.ExpiresAt).Select(item => item.OrderId).Distinct().Take(max).ToListAsync(cancellationToken);
        var closed = 0;
        foreach (var orderId in due)
        {
            closed += await ExpireOrderAsync(orderId, now, cancellationToken);
        }

        return closed;
    }

    /// <summary>Closes the order's overdue proposal the way a decline does: the item is removed and a paid amount refunded.</summary>
    private async Task<int> ExpireOrderAsync(Guid orderId, DateTime now, CancellationToken cancellationToken)
    {
        db.ChangeTracker.Clear();
        var proposal = await db.ReplacementProposals.SingleOrDefaultAsync(item => item.OrderId == orderId && item.Status == "Pending" && item.ExpiresAt < now, cancellationToken);
        if (proposal is null)
        {
            return 0;
        }

        var order = await db.Orders.Include(current => current.Items).SingleAsync(current => current.Id == orderId, cancellationToken);
        var payment = await db.Payments.SingleOrDefaultAsync(item => item.OrderId == orderId, cancellationToken);
        ApplyRemoval(order, payment, proposal, order.UserId, now, "Expired");
        var commit = await SaveAsync(new(ReplacementOutcome.Applied, Proposal: proposal), order, cancellationToken);
        db.ChangeTracker.Clear();
        return commit.Outcome == ReplacementOutcome.Applied ? 1 : 0;
    }

    /// <summary>A declined or expired proposal removes the item. When that would leave the order empty nothing changes; the store cancels it instead.</summary>
    private void ApplyRemoval(Order order, Payment? payment, ReplacementProposal proposal, Guid by, DateTime now, string status)
    {
        proposal.Status = status;
        proposal.DecidedAt = now;
        var removal = OrderChangeRules.Apply(order, payment, [new ItemChange(proposal.LineProductId, proposal.LineVariantId, 0)], status == "Expired" ? "Replacement not answered in time" : "Replacement declined", by, now, out var adjustment);
        if (removal.Status != OrderChangeStatus.Succeeded || adjustment is null)
        {
            return;
        }

        foreach (var gone in order.Items.Where(item => item.Quantity == 0).ToArray())
        {
            order.Items.Remove(gone);
            db.OrderItems.Remove(gone);
        }

        db.OrderAdjustments.Add(adjustment);
    }

    private async Task<ReplacementCommit> SaveAsync(ReplacementCommit commit, Order order, CancellationToken cancellationToken)
    {
        if (commit.Outcome == ReplacementOutcome.Applied || commit.Reason is ReplacementReasons.Expired or ReplacementReasons.OutOfStock)
        {
            var customerId = await db.Customers.Where(customer => customer.UserId == order.UserId).Select(customer => (Guid?)customer.Id).SingleOrDefaultAsync(cancellationToken);
            if (customerId.HasValue && db.ChangeTracker.Entries<OrderAdjustment>().Any(entry => entry.State == EntityState.Added))
            {
                db.Notifications.Add(NotificationCatalog.Create(customerId.Value, order.Id, NotificationTypes.OrderChanged, order.OrderNumber));
            }
        }

        try
        {
            await db.SaveChangesAsync(cancellationToken);
            return commit;
        }
        catch (Exception ex) when (ex is DbUpdateConcurrencyException || SqlErrors.IsDeadlock(ex))
        {
            db.ChangeTracker.Clear();
            return new(ReplacementOutcome.Conflict, "The order changed at the same moment. Open it again and try again.");
        }
    }
}
