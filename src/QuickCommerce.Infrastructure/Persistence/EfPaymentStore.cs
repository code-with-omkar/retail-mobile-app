using Microsoft.EntityFrameworkCore;
using QuickCommerce.Application.Interfaces;
using QuickCommerce.Application.Services;
using QuickCommerce.Domain;

namespace QuickCommerce.Infrastructure.Persistence;

/// <summary>
/// Payments on SQL Server. Each change to an order and its payment is one SaveChanges, so it all happens or none of it does. Two things
/// reaching the same order at once (the app confirming while the provider's notification arrives, the customer cancelling while the payment
/// lands) are told apart by the row versions: the loser starts again on fresh data and finds the winner's result.
/// </summary>
public sealed class EfPaymentStore(QuickCommerceDbContext db) : IPaymentStore
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
            catch (DbUpdateConcurrencyException) when (attempt < Attempts)
            {
                db.ChangeTracker.Clear();
            }
        }
    }

    public Task<PaymentOrderView?> GetOrderAsync(Guid orderId, Guid userId, Guid organizationId, CancellationToken cancellationToken = default) => db.Orders
        .AsNoTracking()
        .Where(order => order.Id == orderId && order.UserId == userId && db.Stores.Any(store => store.Id == order.StoreId && store.OrganizationId == organizationId))
        .Select(order => new PaymentOrderView(order.Id, order.OrderNumber, order.Status, order.PaymentMethod, order.PaymentStatus, order.TotalAmount, order.PaymentExpiresAt))
        .FirstOrDefaultAsync(cancellationToken);

    public Task<Payment?> GetPaymentAsync(Guid orderId, CancellationToken cancellationToken = default) =>
        db.Payments.AsNoTracking().FirstOrDefaultAsync(payment => payment.OrderId == orderId, cancellationToken);

    public Task<Payment?> BeginAttemptAsync(Guid orderId, DateTime now, CancellationToken cancellationToken = default) => RetryAsync<Payment?>(async () =>
    {
        var order = await db.Orders.FirstOrDefaultAsync(item => item.Id == orderId, cancellationToken);
        var payment = await db.Payments.FirstOrDefaultAsync(item => item.OrderId == orderId, cancellationToken);
        if (order is null || payment is null || order.Status != OrderStatus.AwaitingPayment)
        {
            return null;
        }

        payment.Attempts++;
        payment.UpdatedAt = now;
        if (payment.Status == PaymentState.Failed)
        {
            payment.Status = PaymentState.Created;
            order.PaymentStatus = PaymentState.Created;
        }

        await db.SaveChangesAsync(cancellationToken);
        db.ChangeTracker.Clear();
        return payment;
    });

    public async Task<bool> AttachProviderOrderAsync(Guid paymentId, string providerOrderId, CancellationToken cancellationToken = default) =>
        // Only one request can set it: the others change nothing and read the winner's value.
        await db.Payments.Where(payment => payment.Id == paymentId && payment.ProviderOrderId == null)
            .ExecuteUpdateAsync(set => set.SetProperty(payment => payment.ProviderOrderId, providerOrderId), cancellationToken) > 0;

    public Task<CapturedOutcome> ApplyCapturedAsync(string providerOrderId, string providerPaymentId, long paidPaise, DateTime now, CancellationToken cancellationToken = default) => RetryAsync(async () =>
    {
        var payment = await db.Payments.FirstOrDefaultAsync(item => item.ProviderOrderId == providerOrderId, cancellationToken);
        if (payment is null)
        {
            return CapturedOutcome.UnknownPayment;
        }

        var order = await db.Orders.SingleAsync(item => item.Id == payment.OrderId, cancellationToken);
        var result = PaymentTransitions.ApplyCaptured(order, payment, providerPaymentId, paidPaise, now);
        await AddNoteAsync(order, result.Note, cancellationToken);
        if (result.Outcome != CapturedOutcome.AlreadyPaid)
        {
            await db.SaveChangesAsync(cancellationToken);
        }

        db.ChangeTracker.Clear();
        return result.Outcome;
    });

    public Task<bool> ApplyFailedAsync(string providerOrderId, string? providerPaymentId, string reason, DateTime now, CancellationToken cancellationToken = default) => RetryAsync(async () =>
    {
        var payment = await db.Payments.FirstOrDefaultAsync(item => item.ProviderOrderId == providerOrderId, cancellationToken);
        if (payment is null)
        {
            return false;
        }

        var order = await db.Orders.SingleAsync(item => item.Id == payment.OrderId, cancellationToken);
        if (PaymentTransitions.ApplyFailed(order, payment, providerPaymentId, reason, now))
        {
            await db.SaveChangesAsync(cancellationToken);
        }

        db.ChangeTracker.Clear();
        return true;
    });

    public async Task<IReadOnlyList<Guid>> ReleaseExpiredAsync(DateTime now, int max, CancellationToken cancellationToken = default)
    {
        var due = await db.Orders.AsNoTracking()
            .Where(order => order.Status == OrderStatus.AwaitingPayment && order.PaymentExpiresAt <= now)
            .OrderBy(order => order.PaymentExpiresAt)
            .Select(order => order.Id)
            .Take(max)
            .ToListAsync(cancellationToken);
        var released = new List<Guid>();
        foreach (var orderId in due)
        {
            try
            {
                if (await RetryAsync(() => ReleaseOneAsync(orderId, now, cancellationToken)))
                {
                    released.Add(orderId);
                }
            }
            catch (DbUpdateException)
            {
                // Lost the race for this order for good: whoever won has dealt with it. The next run looks again.
                db.ChangeTracker.Clear();
            }
        }

        return released;
    }

    private async Task<bool> ReleaseOneAsync(Guid orderId, DateTime now, CancellationToken cancellationToken)
    {
        var order = await db.Orders.Include(item => item.Items).FirstOrDefaultAsync(item => item.Id == orderId, cancellationToken);
        // Paid or cancelled since we looked: nothing to release.
        if (order is null || order.Status != OrderStatus.AwaitingPayment || order.PaymentExpiresAt > now)
        {
            db.ChangeTracker.Clear();
            return false;
        }

        var payment = await db.Payments.FirstOrDefaultAsync(item => item.OrderId == orderId, cancellationToken);
        var note = PaymentTransitions.ExpireHold(order, payment, now);
        foreach (var line in order.Items.Where(item => item.VariantId.HasValue).GroupBy(item => item.VariantId!.Value))
        {
            var row = await db.StoreVariantInventory.SingleOrDefaultAsync(item => item.StoreId == order.StoreId && item.VariantId == line.Key, cancellationToken);
            if (row is not null)
            {
                row.AvailableQuantity += line.Sum(item => item.Quantity);
            }
        }

        await AddNoteAsync(order, note, cancellationToken);
        await db.SaveChangesAsync(cancellationToken);
        db.ChangeTracker.Clear();
        return true;
    }

    public async Task<IReadOnlyList<Payment>> GetRefundsToStartAsync(int max, CancellationToken cancellationToken = default) => await db.Payments.AsNoTracking()
        .Where(payment => payment.Status == PaymentState.Refunding && payment.RefundId == null)
        .OrderBy(payment => payment.UpdatedAt)
        .Take(max)
        .ToListAsync(cancellationToken);

    public Task SetRefundStartedAsync(Guid paymentId, string refundId, bool processed, DateTime now, CancellationToken cancellationToken = default) => RetryAsync(async () =>
    {
        var payment = await db.Payments.SingleAsync(item => item.Id == paymentId, cancellationToken);
        var order = await db.Orders.SingleAsync(item => item.Id == payment.OrderId, cancellationToken);
        payment.RefundId = refundId;
        payment.UpdatedAt = now;
        if (processed)
        {
            await AddNoteAsync(order, PaymentTransitions.ApplyRefundResult(order, payment, refundId, true, now), cancellationToken);
        }

        await db.SaveChangesAsync(cancellationToken);
        db.ChangeTracker.Clear();
        return true;
    });

    public Task SetRefundFailedAsync(Guid paymentId, string reason, bool permanent, DateTime now, CancellationToken cancellationToken = default) => RetryAsync(async () =>
    {
        var payment = await db.Payments.SingleAsync(item => item.Id == paymentId, cancellationToken);
        payment.FailureReason = reason.Length <= 200 ? reason : reason[..200];
        payment.UpdatedAt = now;
        if (permanent)
        {
            payment.Status = PaymentState.RefundFailed;
            var order = await db.Orders.SingleAsync(item => item.Id == payment.OrderId, cancellationToken);
            order.PaymentStatus = PaymentState.RefundFailed;
        }

        await db.SaveChangesAsync(cancellationToken);
        db.ChangeTracker.Clear();
        return true;
    });

    public Task<bool> ApplyRefundResultAsync(string providerPaymentId, string refundId, bool processed, DateTime now, CancellationToken cancellationToken = default) => RetryAsync(async () =>
    {
        var payment = await db.Payments.FirstOrDefaultAsync(item => item.ProviderPaymentId == providerPaymentId, cancellationToken);
        if (payment is null)
        {
            return false;
        }

        var order = await db.Orders.SingleAsync(item => item.Id == payment.OrderId, cancellationToken);
        await AddNoteAsync(order, PaymentTransitions.ApplyRefundResult(order, payment, refundId, processed, now), cancellationToken);
        await db.SaveChangesAsync(cancellationToken);
        db.ChangeTracker.Clear();
        return true;
    });

    public async Task<IReadOnlyList<Payment>> GetUnsettledAsync(DateTime olderThan, int max, CancellationToken cancellationToken = default)
    {
        var weekAgo = olderThan.AddDays(-7);
        return await db.Payments.AsNoTracking()
            .Where(payment => payment.UpdatedAt < olderThan && payment.CreatedAt > weekAgo &&
                (((payment.Status == PaymentState.Created || payment.Status == PaymentState.Failed) && payment.ProviderOrderId != null) ||
                 (payment.Status == PaymentState.Refunding && payment.RefundId != null)))
            .OrderBy(payment => payment.UpdatedAt)
            .Take(max)
            .ToListAsync(cancellationToken);
    }

    public async Task<bool> TryRecordEventAsync(string provider, string eventId, string type, DateTime now, CancellationToken cancellationToken = default)
    {
        db.PaymentEvents.Add(new PaymentEvent { Provider = provider, EventId = eventId, Type = type.Length <= 64 ? type : type[..64], ReceivedAt = now });
        try
        {
            await db.SaveChangesAsync(cancellationToken);
            return true;
        }
        catch (DbUpdateException)
        {
            // Recorded before (the unique index says so).
            db.ChangeTracker.Clear();
            return false;
        }
    }

    /// <summary>A notification for the customer, added to the change being saved.</summary>
    private async Task AddNoteAsync(Order order, NoteText? note, CancellationToken cancellationToken)
    {
        if (note is null)
        {
            return;
        }

        var customerId = await db.Customers.Where(customer => customer.UserId == order.UserId).Select(customer => (Guid?)customer.Id).FirstOrDefaultAsync(cancellationToken);
        if (customerId.HasValue)
        {
            db.Notifications.Add(new Notification { CustomerId = customerId.Value, OrderId = order.Id, Type = "PaymentUpdate", Title = note.Title, Message = note.Message });
        }
    }
}
