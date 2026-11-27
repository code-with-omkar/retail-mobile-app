using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using QuickCommerce.Api.Security;
using QuickCommerce.Application.Services;

namespace QuickCommerce.Api.Controllers;

/// <summary>What the portal's Notifications screen shows: coupons, offers and ads about to end or just ended.</summary>
[ApiController]
public sealed class PortalAlertsController(IPortalAlertService alerts) : ControllerBase
{
    [HttpGet("api/admin/alerts")]
    [Authorize(Policy = SecurityPolicies.StockManagement)]
    public async Task<IActionResult> Get(CancellationToken cancellationToken) =>
        await alerts.GetAsync(cancellationToken) is { } found
            ? Ok(new { success = true, data = found })
            : StatusCode(StatusCodes.Status403Forbidden, new { success = false, message = "Alerts are for administrators and store managers.", errors = Array.Empty<string>() });
}
