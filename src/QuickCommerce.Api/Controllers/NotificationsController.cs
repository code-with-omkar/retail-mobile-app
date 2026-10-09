using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using QuickCommerce.Api.Security;
using QuickCommerce.Application.DTOs;
using QuickCommerce.Application.Interfaces;

namespace QuickCommerce.Api.Controllers;

[ApiController]
[Route("api/customer/notifications")]
[Authorize(Policy = SecurityPolicies.Orders)]
public sealed class NotificationsController(INotificationService notificationService) : ControllerBase
{
    [HttpGet]
    public async Task<IActionResult> Get([FromQuery] bool unreadOnly = false, CancellationToken cancellationToken = default)
    {
        var notifications = await notificationService.GetAsync(unreadOnly, cancellationToken);
        return notifications is null
            ? Forbid()
            : Ok(new { success = true, data = notifications });
    }

    /// <summary>How many notifications are unread: small enough to ask often (the bell).</summary>
    [HttpGet("unread-count")]
    public async Task<IActionResult> UnreadCount(CancellationToken cancellationToken)
    {
        var count = await notificationService.GetUnreadCountAsync(cancellationToken);
        return count is null ? Forbid() : Ok(new { success = true, data = new UnreadCountResponse(count.Value) });
    }

    /// <summary>Marks every unread notification as read.</summary>
    [HttpPut("read-all")]
    public async Task<IActionResult> ReadAll(CancellationToken cancellationToken)
    {
        var changed = await notificationService.MarkAllReadAsync(cancellationToken);
        return changed is null ? Forbid() : Ok(new { success = true, data = new { updated = changed.Value } });
    }

    [HttpPut("{notificationId:guid}/read")]
    public async Task<IActionResult> MarkRead(Guid notificationId, MarkNotificationReadRequest request, CancellationToken cancellationToken)
    {
        var updated = await notificationService.SetReadAsync(notificationId, request.IsRead, cancellationToken);
        return updated
            ? Ok(new { success = true })
            : NotFound(new { success = false, message = "Notification not found", errors = Array.Empty<string>() });
    }
}