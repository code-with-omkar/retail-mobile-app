using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using QuickCommerce.Api.Security;
using QuickCommerce.Application.Interfaces;

namespace QuickCommerce.Api.Controllers;

[ApiController]
public sealed class AdsController(IAdService ads) : ControllerBase
{
    /// <summary>Administrators: every ad with how often it was seen and tapped.</summary>
    [HttpGet("api/admin/ads")]
    [Authorize(Policy = SecurityPolicies.AdminManagement)]
    public async Task<IActionResult> List(CancellationToken cancellationToken) => Respond(await ads.ListAsync(cancellationToken));

    [HttpPost("api/admin/ads")]
    [Authorize(Policy = SecurityPolicies.AdminManagement)]
    public async Task<IActionResult> Create(SaveAdRequest request, CancellationToken cancellationToken)
    {
        var result = await ads.CreateAsync(request, cancellationToken);
        return result.Status == AdServiceStatus.Succeeded ? Created($"/api/admin/ads/{result.Value!.Id}", new { success = true, data = result.Value }) : Respond(result);
    }

    [HttpPut("api/admin/ads/{id:guid}")]
    [Authorize(Policy = SecurityPolicies.AdminManagement)]
    public async Task<IActionResult> Update(Guid id, SaveAdRequest request, CancellationToken cancellationToken) => Respond(await ads.UpdateAsync(id, request, cancellationToken));

    [HttpDelete("api/admin/ads/{id:guid}")]
    [Authorize(Policy = SecurityPolicies.AdminManagement)]
    public async Task<IActionResult> Delete(Guid id, CancellationToken cancellationToken) => Respond(await ads.DeleteAsync(id, cancellationToken));

    /// <summary>The ads running now at a place (home, home_middle, categories, search, cart, order, product), for one store when given. Open to guests.</summary>
    [HttpGet("api/ads")]
    [AllowAnonymous]
    public async Task<IActionResult> ForPlacement([FromQuery] string? placement, [FromQuery] Guid? storeId, CancellationToken cancellationToken) =>
        Ok(new { success = true, data = await ads.ForPlacementAsync(placement, storeId, cancellationToken) });

    /// <summary>The app showed the ad. Counts one view; always answers 204 so a missing ad is not revealed.</summary>
    [HttpPost("api/ads/{id:guid}/view")]
    [AllowAnonymous]
    [EnableRateLimiting(RateLimitPolicies.Analytics)]
    public async Task<IActionResult> View(Guid id, CancellationToken cancellationToken)
    {
        await ads.RecordViewAsync(id, cancellationToken);
        return NoContent();
    }

    /// <summary>The customer tapped the ad. Counts one tap.</summary>
    [HttpPost("api/ads/{id:guid}/tap")]
    [AllowAnonymous]
    [EnableRateLimiting(RateLimitPolicies.Analytics)]
    public async Task<IActionResult> Tap(Guid id, CancellationToken cancellationToken)
    {
        await ads.RecordTapAsync(id, cancellationToken);
        return NoContent();
    }

    private IActionResult Respond<T>(AdResult<T> result)
    {
        var body = new { success = result.Status == AdServiceStatus.Succeeded, data = result.Value, message = result.Message, errors = Array.Empty<string>() };
        return result.Status switch
        {
            AdServiceStatus.Succeeded => Ok(body),
            AdServiceStatus.Forbidden => StatusCode(StatusCodes.Status403Forbidden, body),
            AdServiceStatus.NotFound => NotFound(body),
            _ => BadRequest(body)
        };
    }
}
