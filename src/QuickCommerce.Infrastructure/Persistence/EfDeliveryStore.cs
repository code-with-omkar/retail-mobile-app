using Microsoft.EntityFrameworkCore;
using QuickCommerce.Application.Interfaces;
using QuickCommerce.Application.Services;
using QuickCommerce.Domain;

namespace QuickCommerce.Infrastructure.Persistence;

/// <summary>
/// Delivery partners on SQL Server. Each step changes the order (and writes the customer's notification) in one save; two people reaching the
/// same order at once are told apart by its row version, and the loser asks again against what the winner wrote.
/// </summary>
public sealed class EfDeliveryStore(QuickCommerceDbContext db) : IDeliveryStore
{
    private const int Attempts = 4;

    private async Task<DeliveryChange> RetryAsync(Func<Task<DeliveryChange>> work)
    {
        for (var attempt = 1; ; attempt++)
        {
            try
            {
                return await work();
            }
            catch (Exception ex) when (ex is DbUpdateConcurrencyException || SqlErrors.IsDeadlock(ex))
            {
                db.ChangeTracker.Clear();
                if (attempt >= Attempts)
                {
                    return new DeliveryChange(DeliveryChangeStatus.Conflict);
                }
            }
        }
    }

    public Task<User?> GetPartnerAsync(Guid userId, CancellationToken cancellationToken = default) =>
        db.Users.AsNoTracking().FirstOrDefaultAsync(user => user.Id == userId && user.Role == Role.DeliveryPartner, cancellationToken);

    public async Task<IReadOnlyList<User>> ListPartnersAsync(Guid organizationId, Guid storeId, CancellationToken cancellationToken = default) =>
        await db.Users.AsNoTracking()
            .Where(user => user.Role == Role.DeliveryPartner && user.IsActive && user.OrganizationId == organizationId && user.StoreId == storeId)
            .ToListAsync(cancellationToken);

    public async Task<IReadOnlyDictionary<Guid, int>> CountActiveAsync(IReadOnlyCollection<Guid> partnerIds, CancellationToken cancellationToken = default) =>
        await db.Orders.AsNoTracking()
            .Where(order => order.DeliveryPartnerId != null && partnerIds.Contains(order.DeliveryPartnerId.Value) && (order.Status == OrderStatus.Ready || order.Status == OrderStatus.OutForDelivery))
            .GroupBy(order => order.DeliveryPartnerId!.Value)
            .Select(group => new { group.Key, Count = group.Count() })
            .ToDictionaryAsync(row => row.Key, row => row.Count, cancellationToken);

    public Task<DeliveryChange> AssignAsync(Guid orderId, Guid organizationId, Guid? scopeStoreId, Guid partnerId, string otp, DateTime now, CancellationToken cancellationToken = default) => RetryAsync(async () =>
    {
        var order = await db.Orders
            .Include(item => item.Items)
            .Include(item => item.StatusHistory)
            .SingleOrDefaultAsync(item => item.Id == orderId && (!scopeStoreId.HasValue || item.StoreId == scopeStoreId.Value) &&
                db.Stores.Any(store => store.Id == item.StoreId && store.OrganizationId == organizationId), cancellationToken);
        if (order is null)
        {
            return new DeliveryChange(DeliveryChangeStatus.NotFound);
        }

        var partnerOk = await db.Users.AnyAsync(user => user.Id == partnerId && user.Role == Role.DeliveryPartner && user.IsActive && user.OrganizationId == organizationId && user.StoreId == order.StoreId, cancellationToken);
        if (!partnerOk)
        {
            return new DeliveryChange(DeliveryChangeStatus.PartnerInvalid);
        }

        var status = DeliveryRules.Assign(order, partnerId, otp, now);
        if (status == DeliveryChangeStatus.Succeeded)
        {
            await db.SaveChangesAsync(cancellationToken);
        }

        return new DeliveryChange(status, order);
    });

    public async Task<IReadOnlyList<(Order Order, Store Store)>> ListForPartnerAsync(Guid partnerId, DateTime deliveredSince, CancellationToken cancellationToken = default)
    {
        var rows = await db.Orders.AsNoTracking()
            .Include(order => order.Items)
            .Where(order => order.DeliveryPartnerId == partnerId &&
                (order.Status == OrderStatus.Ready || order.Status == OrderStatus.OutForDelivery || (order.Status == OrderStatus.Delivered && order.DeliveredAt >= deliveredSince)))
            .OrderByDescending(order => order.AssignedAt)
            .ToListAsync(cancellationToken);
        var storeIds = rows.Select(order => order.StoreId).Distinct().ToArray();
        var stores = await db.Stores.AsNoTracking().Where(store => storeIds.Contains(store.Id)).ToDictionaryAsync(store => store.Id, cancellationToken);
        return rows.Select(order => (order, stores[order.StoreId])).ToArray();
    }

    public Task<DeliveryChange> PickUpAsync(Guid orderId, Guid partnerId, DateTime now, CancellationToken cancellationToken = default) => RetryAsync(async () =>
    {
        var order = await db.Orders.Include(item => item.Items).Include(item => item.StatusHistory)
            .SingleOrDefaultAsync(item => item.Id == orderId && item.DeliveryPartnerId == partnerId, cancellationToken);
        if (order is null)
        {
            return new DeliveryChange(DeliveryChangeStatus.NotFound);
        }

        var status = DeliveryRules.PickUp(order, partnerId, now);
        if (status == DeliveryChangeStatus.Succeeded)
        {
            await NotifyAsync(order, OrderStatus.OutForDelivery, cancellationToken);
            await db.SaveChangesAsync(cancellationToken);
        }

        return new DeliveryChange(status, order);
    });

    public Task<DeliveryChange> DeliverAsync(Guid orderId, Guid partnerId, string otp, decimal cashCollected, DateTime now, CancellationToken cancellationToken = default) => RetryAsync(async () =>
    {
        var order = await db.Orders.Include(item => item.Items).Include(item => item.StatusHistory)
            .SingleOrDefaultAsync(item => item.Id == orderId && item.DeliveryPartnerId == partnerId, cancellationToken);
        if (order is null)
        {
            return new DeliveryChange(DeliveryChangeStatus.NotFound);
        }

        var status = DeliveryRules.Deliver(order, partnerId, otp, cashCollected, now);
        if (status == DeliveryChangeStatus.Succeeded)
        {
            await NotifyAsync(order, OrderStatus.Delivered, cancellationToken);
        }

        // A wrong code is saved too: the count of wrong tries must survive.
        if (status is DeliveryChangeStatus.Succeeded or DeliveryChangeStatus.WrongOtp or DeliveryChangeStatus.Locked)
        {
            await db.SaveChangesAsync(cancellationToken);
        }

        return new DeliveryChange(status, order);
    });

    private async Task NotifyAsync(Order order, OrderStatus status, CancellationToken cancellationToken)
    {
        var customerId = await db.Customers.Where(customer => customer.UserId == order.UserId).Select(customer => (Guid?)customer.Id).SingleOrDefaultAsync(cancellationToken);
        if (customerId.HasValue)
        {
            if (NotificationCatalog.SendsBell(status))
            {
                db.Notifications.Add(NotificationCatalog.ForStatus(customerId.Value, order, status));
            }
        }
    }
}
