using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using QuickCommerce.Api.Security;
using QuickCommerce.Application.Interfaces;

namespace QuickCommerce.Api.Controllers;

/// <summary>Offers and announcements for customers, written and scheduled by administrators.</summary>
[ApiController]
[Route("api/admin/campaigns")]
[Authorize(Policy = SecurityPolicies.AdminManagement)]
public sealed class CampaignsController(ICampaignAdminService campaigns) : ControllerBase
{
    [HttpGet]
    public async Task<IActionResult> List(CancellationToken cancellationToken) => Respond(await campaigns.ListAsync(cancellationToken));

    [HttpPost]
    public async Task<IActionResult> Create(CampaignRequest request, CancellationToken cancellationToken)
    {
        var result = await campaigns.CreateAsync(request, cancellationToken);
        return result.Status == CampaignOperationStatus.Succeeded ? Created($"/api/admin/campaigns/{result.Value!.Id}", new { success = true, data = result.Value }) : Respond(result);
    }

    [HttpPost("{id:guid}/cancel")]
    public async Task<IActionResult> Cancel(Guid id, CancellationToken cancellationToken) => Respond(await campaigns.CancelAsync(id, cancellationToken));

    private IActionResult Respond<T>(CampaignOperationResult<T> result) => result.Status switch
    {
        CampaignOperationStatus.Succeeded => Ok(new { success = true, data = result.Value }),
        CampaignOperationStatus.InvalidRequest => BadRequest(Failure(result)),
        CampaignOperationStatus.Unauthorized => Forbid(),
        CampaignOperationStatus.NotFound => NotFound(Failure(result)),
        CampaignOperationStatus.Conflict => Conflict(Failure(result)),
        _ => StatusCode(StatusCodes.Status500InternalServerError)
    };

    private static object Failure<T>(CampaignOperationResult<T> result) => new { success = false, message = result.Message, reason = result.Reason, errors = Array.Empty<string>() };
}
