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

    [HttpPut("{notificationId:guid}/read")]
    public async Task<IActionResult> MarkRead(Guid notificationId, MarkNotificationReadRequest request, CancellationToken cancellationToken)
    {
        var updated = await notificationService.SetReadAsync(notificationId, request.IsRead, cancellationToken);
        return updated
            ? Ok(new { success = true })
            : NotFound(new { success = false, message = "Notification not found", errors = Array.Empty<string>() });
    }
}