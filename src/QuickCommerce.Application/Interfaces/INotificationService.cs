using QuickCommerce.Application.DTOs;

namespace QuickCommerce.Application.Interfaces;

public interface INotificationService
{
    Task<IReadOnlyList<NotificationResponse>?> GetAsync(bool unreadOnly, CancellationToken cancellationToken = default);
    Task<bool> SetReadAsync(Guid notificationId, bool isRead, CancellationToken cancellationToken = default);
}