using QuickCommerce.Application.DTOs;
using QuickCommerce.Application.Interfaces;
using QuickCommerce.Domain;

namespace QuickCommerce.Application.Services;

public static class StaffStoreScope
{
    /// <summary>
    /// The store a change to this order is limited to. Null for an administrator (no limit). For staff it is the order's own store when that is
    /// one of theirs, otherwise their primary store, so an order elsewhere is simply not found.
    /// </summary>
    public static async Task<Guid?> ForOrderAsync(UserContext context, Guid orderId, ICommerceStore? orders, CancellationToken cancellationToken)
    {
        if (context.Role != Role.StoreStaff)
        {
            return null;
        }

        var mine = context.OrderStoreIds;
        if (mine.Count > 1 && orders is not null && await orders.GetOrderStoreIdAsync(orderId, cancellationToken) is { } store && mine.Contains(store))
        {
            return store;
        }

        return context.StoreId;
    }
}
