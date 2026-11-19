using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using QuickCommerce.Api.Security;
using QuickCommerce.Application.Interfaces;

namespace QuickCommerce.Api.Controllers;

/// <summary>One store's day at a glance: counts, what needs attention, and the scheduled orders coming up. Store staff (their own store) and administrators. Read only.</summary>
[ApiController]
[Route("api/store-today")]
[Authorize(Policy = SecurityPolicies.StockManagement)]
public sealed class TodayController(ITodayService today) : ControllerBase
{
    [HttpGet]
    public async Task<IActionResult> Get([FromQuery] Guid? storeId, CancellationToken cancellationToken)
    {
        var result = await today.GetAsync(storeId, cancellationToken);
        var body = new { success = result.Status == TodayStatus.Succeeded, data = result.Value, message = result.Message, errors = Array.Empty<string>() };
        return result.Status switch
        {
            TodayStatus.Succeeded => Ok(body),
            TodayStatus.Forbidden => StatusCode(StatusCodes.Status403Forbidden, body),
            TodayStatus.NotFound => NotFound(body),
            _ => BadRequest(body)
        };
    }
}
