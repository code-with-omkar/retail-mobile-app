namespace QuickCommerce.Application.DTOs;

public sealed record NotificationResponse(
    Guid Id,
    Guid? OrderId,
    string Type,
    string Title,
    string Message,
    bool IsRead,
    DateTime CreatedAt);

public sealed record MarkNotificationReadRequest(bool IsRead = true);