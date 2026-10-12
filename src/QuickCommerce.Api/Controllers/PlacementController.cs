using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using QuickCommerce.Api.Security;
using QuickCommerce.Application.Interfaces;

namespace QuickCommerce.Api.Controllers;

/// <summary>
/// Paid placement: stores bid for first place in a pincode for a week and pay up front; the highest paid bid leads. Store staff bid for
/// their own store, an administrator for any store and sets the rules.
/// </summary>
[ApiController]
[Route("api/placement")]
[Authorize(Policy = SecurityPolicies.OfferManagement)]
public sealed class PlacementController(IPlacementService placement) : ControllerBase
{
    [HttpGet("rules")]
    public async Task<IActionResult> Rules(CancellationToken cancellationToken) => Respond(await placement.GetRulesAsync(cancellationToken));

    [HttpPut("rules")]
    [Authorize(Policy = SecurityPolicies.AdminManagement)]
    public async Task<IActionResult> SetRules(SetPlacementRulesRequest request, CancellationToken cancellationToken) => Respond(await placement.SetRulesAsync(request, cancellationToken));

    /// <summary>The upcoming weeks of a pincode: what leads each and the least a new bid must be.</summary>
    [HttpGet("market")]
    public async Task<IActionResult> Market([FromQuery] string pincode, CancellationToken cancellationToken) => Respond(await placement.GetMarketAsync(pincode, cancellationToken));

    [HttpGet("bids")]
    public async Task<IActionResult> Bids(CancellationToken cancellationToken) => Respond(await placement.ListBidsAsync(cancellationToken));

    [HttpPost("bids")]
    public async Task<IActionResult> PlaceBid(PlaceBidRequest request, CancellationToken cancellationToken)
    {
        var result = await placement.PlaceBidAsync(request, cancellationToken);
        return result.Status == CampaignOperationStatus.Succeeded ? Created($"/api/placement/bids/{result.Value!.Id}", new { success = true, data = result.Value }) : Respond(result);
    }

    [HttpPost("bids/{id:guid}/cancel")]
    public async Task<IActionResult> Cancel(Guid id, CancellationToken cancellationToken) => Respond(await placement.CancelBidAsync(id, cancellationToken));

    /// <summary>Starts or resumes paying a bid: returns what the payment screen needs. No secrets.</summary>
    [HttpPost("bids/{id:guid}/payment")]
    public async Task<IActionResult> StartPayment(Guid id, CancellationToken cancellationToken) => Respond(await placement.StartPaymentAsync(id, cancellationToken));

    /// <summary>The portal reports the result of the payment screen. Repeating it changes nothing.</summary>
    [HttpPost("bids/{id:guid}/payment/confirm")]
    public async Task<IActionResult> ConfirmPayment(Guid id, ConfirmPaymentRequest request, CancellationToken cancellationToken) => Respond(await placement.ConfirmPaymentAsync(id, request, cancellationToken));

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
        CampaignOperationStatus.InvalidRequest => BadRequest(new { success = false, message = result.Message, reason = result.Reason, errors = Array.Empty<string>() }),
        CampaignOperationStatus.Unauthorized => Forbid(),
        CampaignOperationStatus.NotFound => NotFound(new { success = false, message = result.Message, reason = result.Reason, errors = Array.Empty<string>() }),
        CampaignOperationStatus.Conflict => Conflict(new { success = false, message = result.Message, reason = result.Reason, errors = Array.Empty<string>() }),
        _ => StatusCode(StatusCodes.Status500InternalServerError)
    };
}
