using Microsoft.EntityFrameworkCore;
using QuickCommerce.Application.Interfaces;
using QuickCommerce.Application.Services;
using QuickCommerce.Domain;

namespace QuickCommerce.Infrastructure.Persistence;

/// <summary>Order changes on SQL Server. The order, its payment and the adjustment are saved together, so a change and its refund are never apart.</summary>
public sealed class EfOrderChangeStore(QuickCommerceDbContext db) : IOrderChangeStore
{
    public async Task<OrderChangeCommit?> ApplyAsync(Guid orderId, Guid organizationId, Guid? storeId, Guid changedBy, IReadOnlyList<ItemChange> changes, string? reason, DateTime now, CancellationToken cancellationToken = default)
    {
        var order = await db.Orders
            .Include(current => current.Items)
            .SingleOrDefaultAsync(current => current.Id == orderId && (!storeId.HasValue || current.StoreId == storeId.Value) &&
                db.Stores.Any(store => store.Id == current.StoreId && store.OrganizationId == organizationId), cancellationToken);
        if (order is null)
        {
            return null;
        }

        var payment = await db.Payments.SingleOrDefaultAsync(item => item.OrderId == orderId, cancellationToken);
        var before = order.Items.ToArray();
        var commit = OrderChangeRules.Apply(order, payment, changes, reason, changedBy, now, out var adjustment);
        if (commit.Status != OrderChangeStatus.Succeeded)
        {
            return commit;
        }

        foreach (var gone in before.Where(item => item.Quantity == 0))
        {
            order.Items.Remove(gone);
            db.OrderItems.Remove(gone);
        }

        db.OrderAdjustments.Add(adjustment!);
        var customerId = await db.Customers.Where(customer => customer.UserId == order.UserId).Select(customer => (Guid?)customer.Id).SingleOrDefaultAsync(cancellationToken);
        if (customerId.HasValue)
        {
            db.Notifications.Add(NotificationCatalog.Create(customerId.Value, order.Id, NotificationTypes.OrderChanged, order.OrderNumber));
        }

        try
        {
            await db.SaveChangesAsync(cancellationToken);
            return commit;
        }
        catch (Exception ex) when (ex is DbUpdateConcurrencyException || SqlErrors.IsDeadlock(ex))
        {
            // Something else changed the order at the same moment (the customer cancelled, the status moved): nothing was written.
            db.ChangeTracker.Clear();
            return new OrderChangeCommit(OrderChangeStatus.Conflict, "The order changed while you were editing it. Reload it and try again.");
        }
    }

    public async Task<IReadOnlyList<OrderAdjustment>> ListAsync(Guid orderId, CancellationToken cancellationToken = default) =>
        await db.OrderAdjustments.AsNoTracking().Where(item => item.OrderId == orderId).OrderBy(item => item.CreatedAt).ToListAsync(cancellationToken);

    public async Task<IReadOnlyList<(OrderAdjustment Adjustment, string ProviderPaymentId)>> GetRefundsToStartAsync(int max, CancellationToken cancellationToken = default)
    {
        var rows = await db.OrderAdjustments.AsNoTracking()
            .Where(item => item.RefundStatus == "Pending")
            .Join(db.Payments.AsNoTracking().Where(payment => payment.ProviderPaymentId != null), item => item.OrderId, payment => payment.OrderId, (item, payment) => new { item, payment.ProviderPaymentId })
            .OrderBy(row => row.item.CreatedAt).Take(max).ToListAsync(cancellationToken);
        return rows.Select(row => (row.item, row.ProviderPaymentId!)).ToArray();
    }

    public async Task SetRefundStartedAsync(Guid adjustmentId, string refundId, bool processed, CancellationToken cancellationToken = default) =>
        await db.OrderAdjustments.Where(item => item.Id == adjustmentId && item.RefundStatus == "Pending")
            .ExecuteUpdateAsync(set => set.SetProperty(item => item.RefundStatus, processed ? "Processed" : "Started").SetProperty(item => item.RefundId, refundId).SetProperty(item => item.RefundFailure, (string?)null), cancellationToken);

    public async Task SetRefundFailedAsync(Guid adjustmentId, string reason, bool permanent, CancellationToken cancellationToken = default)
    {
        var text = reason.Length > 200 ? reason[..200] : reason;
        await db.OrderAdjustments.Where(item => item.Id == adjustmentId && item.RefundStatus == "Pending")
            .ExecuteUpdateAsync(set => set.SetProperty(item => item.RefundStatus, permanent ? "Failed" : "Pending").SetProperty(item => item.RefundFailure, text), cancellationToken);
    }

    public async Task<IReadOnlyList<(OrderAdjustment Adjustment, string ProviderPaymentId, long RefundedBefore)>> GetStartedRefundsAsync(int max, CancellationToken cancellationToken = default)
    {
        var started = await db.OrderAdjustments.AsNoTracking().Where(item => item.RefundStatus == "Started").OrderBy(item => item.CreatedAt).Take(max).ToListAsync(cancellationToken);
        var result = new List<(OrderAdjustment, string, long)>();
        foreach (var item in started)
        {
            var providerPaymentId = await db.Payments.AsNoTracking().Where(payment => payment.OrderId == item.OrderId).Select(payment => payment.ProviderPaymentId).SingleOrDefaultAsync(cancellationToken);
            if (providerPaymentId is null)
            {
                continue;
            }

            // Everything refunded against this payment before this adjustment counts towards what the provider must already show as refunded.
            var before = await db.OrderAdjustments.AsNoTracking().Where(other => other.OrderId == item.OrderId && other.CreatedAt < item.CreatedAt && (other.RefundStatus == "Started" || other.RefundStatus == "Processed")).SumAsync(other => other.RefundPaise, cancellationToken);
            result.Add((item, providerPaymentId, before));
        }

        return result;
    }

    public async Task MarkRefundProcessedAsync(Guid adjustmentId, CancellationToken cancellationToken = default) =>
        await db.OrderAdjustments.Where(item => item.Id == adjustmentId && item.RefundStatus == "Started")
            .ExecuteUpdateAsync(set => set.SetProperty(item => item.RefundStatus, "Processed"), cancellationToken);
}
