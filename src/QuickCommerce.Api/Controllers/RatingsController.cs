using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using QuickCommerce.Api.Security;
using QuickCommerce.Application.Interfaces;

namespace QuickCommerce.Api.Controllers;

/// <summary>Customers rate the orders they received; store staff and administrators read the ratings. The customer comes from the token.</summary>
[ApiController]
public sealed class RatingsController(IRatingService ratings) : ControllerBase
{
    [HttpGet("api/customer/orders/{orderId:guid}/rating")]
    [Authorize(Policy = SecurityPolicies.Orders)]
    public async Task<IActionResult> Mine(Guid orderId, CancellationToken cancellationToken) => Respond(await ratings.GetMineAsync(orderId, cancellationToken));

    [HttpPut("api/customer/orders/{orderId:guid}/rating")]
    [Authorize(Policy = SecurityPolicies.Orders)]
    public async Task<IActionResult> Save(Guid orderId, RatingRequest request, CancellationToken cancellationToken) => Respond(await ratings.SaveMineAsync(orderId, request, cancellationToken));

    [HttpGet("api/reports/ratings")]
    [Authorize(Policy = SecurityPolicies.StockManagement)]
    public async Task<IActionResult> Report([FromQuery] DateOnly from, [FromQuery] DateOnly to, [FromQuery] Guid? storeId, CancellationToken cancellationToken) =>
        Respond(await ratings.ReportAsync(from, to, storeId, cancellationToken));

    private IActionResult Respond<T>(RatingResult<T> result)
    {
        var body = new { success = result.Status == RatingStatus.Succeeded, data = result.Value, message = result.Message, errors = Array.Empty<string>() };
        return result.Status switch
        {
            RatingStatus.Succeeded => Ok(body),
            RatingStatus.Forbidden => StatusCode(StatusCodes.Status403Forbidden, body),
            RatingStatus.NotFound => NotFound(body),
            RatingStatus.NotYet or RatingStatus.TooLate => Conflict(body),
            _ => BadRequest(body)
        };
    }
}
