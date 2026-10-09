using QuickCommerce.Application.DTOs;
using QuickCommerce.Application.Interfaces;
using QuickCommerce.Domain;

namespace QuickCommerce.Application.Services;

public sealed class NotificationService(
    ICommerceStore data,
    ICurrentUserContextResolver currentUserContextResolver) : INotificationService
{
    public async Task<IReadOnlyList<NotificationResponse>?> GetAsync(bool unreadOnly, string? language = null, CancellationToken cancellationToken = default)
    {
        var customer = await ResolveCustomerAsync(cancellationToken);
        if (customer is null)
        {
            return null;
        }

        var notifications = await data.GetNotificationsAsync(customer.Id, unreadOnly, cancellationToken);
        return notifications.OrderByDescending(notification => notification.CreatedAt).Select(notification => Map(notification, language)).ToArray();
    }

    public async Task<bool> SetReadAsync(Guid notificationId, bool isRead, CancellationToken cancellationToken = default)
    {
        var customer = await ResolveCustomerAsync(cancellationToken);
        return customer is not null && await data.SetNotificationReadAsync(notificationId, customer.Id, isRead, cancellationToken);
    }

    public async Task<int?> GetUnreadCountAsync(CancellationToken cancellationToken = default)
    {
        var customer = await ResolveCustomerAsync(cancellationToken);
        return customer is null ? null : await data.GetUnreadNotificationCountAsync(customer.Id, cancellationToken);
    }

    public async Task<int?> MarkAllReadAsync(CancellationToken cancellationToken = default)
    {
        var customer = await ResolveCustomerAsync(cancellationToken);
        return customer is null ? null : await data.MarkAllNotificationsReadAsync(customer.Id, cancellationToken);
    }

    public async Task<NotificationPreferences?> GetPreferencesAsync(CancellationToken cancellationToken = default)
    {
        var customer = await ResolveCustomerAsync(cancellationToken);
        return customer is null ? null : new NotificationPreferences(customer.OffersEnabled);
    }

    public async Task<NotificationPreferences?> SetOffersAsync(bool enabled, CancellationToken cancellationToken = default)
    {
        var customer = await ResolveCustomerAsync(cancellationToken);
        if (customer is null)
        {
            return null;
        }

        await data.SetOffersEnabledAsync(customer.Id, enabled, cancellationToken);
        return new NotificationPreferences(enabled);
    }

    private async Task<Customer?> ResolveCustomerAsync(CancellationToken cancellationToken)
    {
        var context = await currentUserContextResolver.ResolveAsync(cancellationToken);
        return context is { Role: Role.Customer }
            ? await data.GetCustomerByUserIdAsync(context.UserId, cancellationToken)
            : null;
    }

    private static NotificationResponse Map(Notification notification, string? language)
    {
        var (title, message) = NotificationCatalog.Render(notification, language);
        return new NotificationResponse(
            notification.Id,
            notification.OrderId,
            notification.Type,
            title,
            message,
            notification.IsRead,
            DateTime.SpecifyKind(notification.CreatedAt, DateTimeKind.Utc),
            notification.Category);
    }
}