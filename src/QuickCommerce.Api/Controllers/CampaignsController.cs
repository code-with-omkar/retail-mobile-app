using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using QuickCommerce.Api.Security;
using QuickCommerce.Application.Interfaces;

namespace QuickCommerce.Api.Controllers;

/// <summary>Offers and announcements for customers: written and scheduled by administrators, or by store staff for their own store.</summary>
[ApiController]
[Route("api/admin/campaigns")]
[Authorize(Policy = SecurityPolicies.OfferManagement)]
public sealed class CampaignsController(ICampaignAdminService campaigns, ICampaignPaymentService payments) : ControllerBase
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

    /// <summary>Starts or resumes paying the fee of an offer: returns what the payment screen needs. No secrets.</summary>
    [HttpPost("{id:guid}/payment")]
    public async Task<IActionResult> StartPayment(Guid id, CancellationToken cancellationToken) => Respond(await payments.StartAsync(id, cancellationToken));

    /// <summary>The portal reports the result of the payment screen. Repeating it changes nothing.</summary>
    [HttpPost("{id:guid}/payment/confirm")]
    public async Task<IActionResult> ConfirmPayment(Guid id, ConfirmPaymentRequest request, CancellationToken cancellationToken) => Respond(await payments.ConfirmAsync(id, request, cancellationToken));

    private IActionResult Respond<T>(PaymentOperationResult<T> result) => result.Status switch
    {
        PaymentOperationStatus.Succeeded => Ok(new { success = true, data = result.Value }),
        PaymentOperationStatus.InvalidRequest => BadRequest(new { success = false, message = result.Message, reason = result.Reason, errors = Array.Empty<string>() }),
        PaymentOperationStatus.Unauthorized => Forbid(),
        PaymentOperationStatus.NotFound => NotFound(new { success = false, message = result.Message, reason = result.Reason, errors = Array.Empty<string>() }),
        PaymentOperationStatus.Conflict => Conflict(new { success = false, message = result.Message, reason = result.Reason, errors = Array.Empty<string>() }),
        _ => StatusCode(StatusCodes.Status500InternalServerError)
    };

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
