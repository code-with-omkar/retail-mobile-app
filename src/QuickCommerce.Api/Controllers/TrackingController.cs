using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using QuickCommerce.Api.Security;
using QuickCommerce.Application.Interfaces;

namespace QuickCommerce.Api.Controllers;

/// <summary>Delivery tracking: the partner shares a position, the customer reads distance and ETA.</summary>
[ApiController]
public sealed class TrackingController(ITrackingService tracking) : ControllerBase
{
    [HttpPut("api/delivery/{orderId:guid}/location")]
    [Authorize(Policy = SecurityPolicies.DeliveryPartner)]
    public async Task<IActionResult> Share(Guid orderId, ShareLocationRequest request, CancellationToken cancellationToken) => Respond(await tracking.ShareAsync(orderId, request, cancellationToken));

    [HttpGet("api/customer/orders/{orderId:guid}/tracking")]
    [Authorize(Policy = SecurityPolicies.Orders)]
    public async Task<IActionResult> Get(Guid orderId, CancellationToken cancellationToken) => Respond(await tracking.GetAsync(orderId, cancellationToken));

    private IActionResult Respond<T>(TrackingResult<T> result)
    {
        var body = new { success = result.Status == TrackingStatus.Succeeded, data = result.Value, message = result.Message, errors = Array.Empty<string>() };
        return result.Status switch
        {
            TrackingStatus.Succeeded => Ok(body),
            TrackingStatus.Forbidden => StatusCode(StatusCodes.Status403Forbidden, body),
            TrackingStatus.NotFound => NotFound(body),
            TrackingStatus.Invalid => BadRequest(body),
            _ => Conflict(body)
        };
    }
}
