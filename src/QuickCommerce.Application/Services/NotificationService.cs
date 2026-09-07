using QuickCommerce.Application.DTOs;
using QuickCommerce.Application.Interfaces;
using QuickCommerce.Domain;

namespace QuickCommerce.Application.Services;

public sealed class NotificationService(
    ICommerceStore data,
    ICurrentUserContextResolver currentUserContextResolver) : INotificationService
{
    public async Task<IReadOnlyList<NotificationResponse>?> GetAsync(bool unreadOnly, CancellationToken cancellationToken = default)
    {
        var customer = await ResolveCustomerAsync(cancellationToken);
        if (customer is null)
        {
            return null;
        }

        var notifications = await data.GetNotificationsAsync(customer.Id, unreadOnly, cancellationToken);
        return notifications.OrderByDescending(notification => notification.CreatedAt).Select(Map).ToArray();
    }

    public async Task<bool> SetReadAsync(Guid notificationId, bool isRead, CancellationToken cancellationToken = default)
    {
        var customer = await ResolveCustomerAsync(cancellationToken);
        return customer is not null && await data.SetNotificationReadAsync(notificationId, customer.Id, isRead, cancellationToken);
    }

    private async Task<Customer?> ResolveCustomerAsync(CancellationToken cancellationToken)
    {
        var context = await currentUserContextResolver.ResolveAsync(cancellationToken);
        return context is { Role: Role.Customer }
            ? await data.GetCustomerByUserIdAsync(context.UserId, cancellationToken)
            : null;
    }

    private static NotificationResponse Map(Notification notification) => new(
        notification.Id,
        notification.OrderId,
        notification.Type,
        notification.Title,
        notification.Message,
        notification.IsRead,
        notification.CreatedAt);
}