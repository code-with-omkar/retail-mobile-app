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

    /// <summary>Ends a running discount now. The message already sent stays sent.</summary>
    [HttpPost("{id:guid}/end-discount")]
    public async Task<IActionResult> EndDiscount(Guid id, CancellationToken cancellationToken) => Respond(await campaigns.EndDiscountAsync(id, cancellationToken));

    /// <summary>Products to choose for a discount, by name.</summary>
    [HttpGet("products")]
    public async Task<IActionResult> Products([FromQuery] string? search, CancellationToken cancellationToken) => Respond(await campaigns.SearchProductsAsync(search, cancellationToken));

    /// <summary>One banner picture as the form field "file" (JPEG, PNG or WebP, up to 2 MB, judged by content). Returns its path for the offer's imageUrl.</summary>
    [HttpPost("image")]
    [RequestSizeLimit(3 * 1024 * 1024)]
    public async Task<IActionResult> UploadImage(IFormFile? file, CancellationToken cancellationToken)
    {
        if (file is null)
        {
            return BadRequest(new { success = false, message = "Choose a picture.", reason = (string?)null, errors = Array.Empty<string>() });
        }

        await using var stream = file.OpenReadStream();
        var result = await campaigns.UploadImageAsync(stream, file.Length, cancellationToken);
        return result.Status == CampaignOperationStatus.Succeeded ? Ok(new { success = true, data = new { imageUrl = result.Value } }) : Respond(result);
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
