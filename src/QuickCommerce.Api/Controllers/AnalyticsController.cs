using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using QuickCommerce.Api.Security;
using QuickCommerce.Application.Interfaces;

namespace QuickCommerce.Api.Controllers;

[ApiController]
public sealed class AnalyticsController(IAnalyticsService analytics) : ControllerBase
{
    /// <summary>
    /// The app's anonymous steps (search, add to cart, ...) for one session, up to 25 at a time. No sign-in: guests browse too. Nothing personal is
    /// accepted or stored; unknown names and bad session ids are refused.
    /// </summary>
    [HttpPost("api/analytics/events")]
    [AllowAnonymous]
    [EnableRateLimiting(RateLimitPolicies.Analytics)]
    public async Task<IActionResult> Record(AnalyticsBatchRequest request, CancellationToken cancellationToken)
    {
        var result = await analytics.RecordAsync(request, cancellationToken);
        return result.Status == AnalyticsStatus.Succeeded
            ? Accepted(new { success = true, data = result.Value })
            : BadRequest(new { success = false, message = result.Message, errors = Array.Empty<string>() });
    }

    /// <summary>Days are India days: <c>from</c> and <c>to</c> are dates (2026-10-14), both included. Store staff see their own store; an administrator one store or all.</summary>
    [HttpGet("api/reports/behaviour")]
    [Authorize(Policy = SecurityPolicies.StockManagement)]
    public async Task<IActionResult> Report([FromQuery] DateOnly from, [FromQuery] DateOnly to, [FromQuery] Guid? storeId, CancellationToken cancellationToken)
    {
        var result = await analytics.ReportAsync(from, to, storeId, cancellationToken);
        var body = new { success = result.Status == AnalyticsStatus.Succeeded, data = result.Value, message = result.Message, errors = Array.Empty<string>() };
        return result.Status switch
        {
            AnalyticsStatus.Succeeded => Ok(body),
            AnalyticsStatus.Forbidden => StatusCode(StatusCodes.Status403Forbidden, body),
            AnalyticsStatus.NotFound => NotFound(body),
            _ => BadRequest(body)
        };
    }
}
