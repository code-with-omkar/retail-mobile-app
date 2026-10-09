using QuickCommerce.Application.DTOs;

namespace QuickCommerce.Application.Interfaces;

public interface INotificationService
{
    /// <summary>The customer's notifications, newest first, written in [language] ("mr" for Marathi, otherwise English).</summary>
    Task<IReadOnlyList<NotificationResponse>?> GetAsync(bool unreadOnly, string? language = null, CancellationToken cancellationToken = default);

    Task<NotificationPreferences?> GetPreferencesAsync(CancellationToken cancellationToken = default);

    /// <summary>Switches offers and announcements on or off. Order and payment notifications are not affected.</summary>
    Task<NotificationPreferences?> SetOffersAsync(bool enabled, CancellationToken cancellationToken = default);
    Task<bool> SetReadAsync(Guid notificationId, bool isRead, CancellationToken cancellationToken = default);
    Task<int?> GetUnreadCountAsync(CancellationToken cancellationToken = default);
    Task<int?> MarkAllReadAsync(CancellationToken cancellationToken = default);
}