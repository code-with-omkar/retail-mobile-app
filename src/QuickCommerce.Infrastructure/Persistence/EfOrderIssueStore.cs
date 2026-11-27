using Microsoft.EntityFrameworkCore;
using QuickCommerce.Application.Interfaces;
using QuickCommerce.Application.Services;
using QuickCommerce.Domain;

namespace QuickCommerce.Infrastructure.Persistence;

public sealed class EfOrderIssueStore(QuickCommerceDbContext db) : IOrderIssueStore
{
    private const int Listed = 500;

    public async Task<IssueOrder?> GetCustomerOrderAsync(Guid orderId, Guid userId, CancellationToken cancellationToken = default)
    {
        var order = await db.Orders.AsNoTracking().Include(item => item.Items).Include(item => item.StatusHistory)
            .SingleOrDefaultAsync(item => item.Id == orderId && item.UserId == userId, cancellationToken);
        if (order is null)
        {
            return null;
        }

        var organization = await db.Stores.AsNoTracking().Where(store => store.Id == order.StoreId).Select(store => store.OrganizationId).SingleAsync(cancellationToken);
        // Delivered by a partner, or marked complete at the store.
        var delivered = order.DeliveredAt ?? order.StatusHistory.Where(item => item.Status is OrderStatus.Delivered or OrderStatus.Completed).Select(item => (DateTime?)item.ChangedAt).Max();
        return new IssueOrder(order, organization, delivered);
    }

    private async Task<IReadOnlyList<IssueRow>> LoadAsync(IQueryable<OrderIssue> issues, CancellationToken cancellationToken)
    {
        var found = await issues.AsNoTracking().Include(issue => issue.Items).ToListAsync(cancellationToken);
        var orderIds = found.Select(issue => issue.OrderId).ToArray();
        var storeIds = found.Select(issue => issue.StoreId).Distinct().ToArray();
        var userIds = found.Select(issue => issue.CustomerUserId).Distinct().ToArray();
        var numbers = await db.Orders.AsNoTracking().Where(order => orderIds.Contains(order.Id)).ToDictionaryAsync(order => order.Id, order => order.OrderNumber, cancellationToken);
        var stores = await db.Stores.AsNoTracking().Where(store => storeIds.Contains(store.Id)).ToDictionaryAsync(store => store.Id, store => store.Name, cancellationToken);
        var people = await db.Users.AsNoTracking().Where(user => userIds.Contains(user.Id)).ToDictionaryAsync(user => user.Id, user => user.DisplayName, cancellationToken);
        return found.Select(issue => new IssueRow(issue, numbers.GetValueOrDefault(issue.OrderId) ?? string.Empty, stores.GetValueOrDefault(issue.StoreId) ?? string.Empty, people.GetValueOrDefault(issue.CustomerUserId) ?? string.Empty)).ToArray();
    }

    public async Task<IssueRow?> GetByOrderAsync(Guid orderId, CancellationToken cancellationToken = default) =>
        (await LoadAsync(db.OrderIssues.Where(issue => issue.OrderId == orderId), cancellationToken)).FirstOrDefault();

    public async Task<IssueRow?> GetAsync(Guid organizationId, Guid id, CancellationToken cancellationToken = default) =>
        (await LoadAsync(db.OrderIssues.Where(issue => issue.OrganizationId == organizationId && issue.Id == id), cancellationToken)).FirstOrDefault();

    public Task<IReadOnlyList<IssueRow>> ListAsync(Guid organizationId, IReadOnlyCollection<Guid>? storeIds, CancellationToken cancellationToken = default) =>
        LoadAsync(db.OrderIssues.Where(issue => issue.OrganizationId == organizationId && (storeIds == null || storeIds.Contains(issue.StoreId))).OrderByDescending(issue => issue.CreatedAt).Take(Listed), cancellationToken);

    public async Task<bool> AddAsync(OrderIssue issue, CancellationToken cancellationToken = default)
    {
        db.OrderIssues.Add(issue);
        try
        {
            await db.SaveChangesAsync(cancellationToken);
            return true;
        }
        catch (DbUpdateException ex) when (ex.InnerException is Microsoft.Data.SqlClient.SqlException { Number: 2601 or 2627 })
        {
            db.ChangeTracker.Clear();
            return false;
        }
    }

    public async Task<bool> DecideAsync(Guid id, bool approve, decimal amount, string? note, Guid decidedBy, DateTime now, CancellationToken cancellationToken = default)
    {
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        var status = approve ? "Approved" : "Refused";
        // The conditional update is the judge: of two managers deciding at once, one wins.
        if (await db.OrderIssues.Where(issue => issue.Id == id && issue.Status == "Open").ExecuteUpdateAsync(set => set
                .SetProperty(issue => issue.Status, status).SetProperty(issue => issue.DecisionNote, note)
                .SetProperty(issue => issue.DecidedBy, decidedBy).SetProperty(issue => issue.DecidedAt, now), cancellationToken) != 1)
        {
            return false;
        }

        var issue = await db.OrderIssues.AsNoTracking().SingleAsync(item => item.Id == id, cancellationToken);
        var order = await db.Orders.AsNoTracking().SingleAsync(item => item.Id == issue.OrderId, cancellationToken);
        if (approve)
        {
            var payment = await db.Payments.SingleOrDefaultAsync(item => item.OrderId == order.Id, cancellationToken);
            var online = order.PaymentMethod == PaymentMethods.Online && payment is { Status: PaymentState.Paid, ProviderPaymentId: not null };
            if (online)
            {
                var left = (payment!.AmountPaise - payment.PartialRefundedPaise) / 100m;
                amount = Math.Min(amount, Math.Max(0m, left));
                var paise = (long)Math.Round(amount * 100m);
                if (paise > 0)
                {
                    payment.PartialRefundedPaise += paise;
                    // Picked up by the same job that refunds items a store removes.
                    db.OrderAdjustments.Add(new OrderAdjustment
                    {
                        OrderId = order.Id,
                        ChangedBy = decidedBy,
                        CreatedAt = now,
                        Reason = "Refund for a reported problem",
                        OldSubtotal = order.SubtotalAmount,
                        NewSubtotal = order.SubtotalAmount,
                        RefundPaise = paise,
                        RefundStatus = "Pending"
                    });
                }

                await db.SaveChangesAsync(cancellationToken);
            }

            var method = online ? "Online" : "Cash";
            await db.OrderIssues.Where(item => item.Id == id).ExecuteUpdateAsync(set => set.SetProperty(item => item.ApprovedAmount, amount).SetProperty(item => item.RefundMethod, method), cancellationToken);
        }

        if (await db.Customers.Where(customer => customer.UserId == issue.CustomerUserId).Select(customer => (Guid?)customer.Id).SingleOrDefaultAsync(cancellationToken) is { } customerId)
        {
            db.Notifications.Add(NotificationCatalog.Create(customerId, order.Id, approve ? NotificationTypes.IssueApproved : NotificationTypes.IssueRefused, order.OrderNumber));
        }

        await db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return true;
    }

    public async Task<bool> MarkCashPaidAsync(Guid id, Guid paidBy, DateTime now, CancellationToken cancellationToken = default) =>
        await db.OrderIssues.Where(issue => issue.Id == id && issue.Status == "Approved" && issue.RefundMethod == "Cash" && issue.CashPaidAt == null)
            .ExecuteUpdateAsync(set => set.SetProperty(issue => issue.CashPaidAt, now).SetProperty(issue => issue.CashPaidBy, paidBy), cancellationToken) == 1;
}
