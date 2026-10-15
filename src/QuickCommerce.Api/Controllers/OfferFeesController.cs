using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using QuickCommerce.Api.Security;
using QuickCommerce.Application.Interfaces;

namespace QuickCommerce.Api.Controllers;

/// <summary>What a store pays to publish an offer. Administrators set the default and each store's own fee; store staff can read what they would pay.</summary>
[ApiController]
[Route("api/admin/offer-fees")]
public sealed class OfferFeesController(IOfferFeeService fees) : ControllerBase
{
    [HttpGet]
    [Authorize(Policy = SecurityPolicies.AdminManagement)]
    public async Task<IActionResult> Get(CancellationToken cancellationToken) => Respond(await fees.GetAsync(cancellationToken));

    /// <summary>Sets the fee that applies to every store without its own (paise; 0 is free).</summary>
    [HttpPut("default")]
    [Authorize(Policy = SecurityPolicies.AdminManagement)]
    public async Task<IActionResult> SetDefault(SetOfferFeeRequest request, CancellationToken cancellationToken) =>
        request.AmountPaise is null ? BadRequest(new { success = false, message = "Give the fee in paise", errors = Array.Empty<string>() }) : Respond(await fees.SetDefaultAsync(request.AmountPaise.Value, cancellationToken));

    /// <summary>Sets the extra fee, in paise, for an offer that is also shown when the app opens (0 is free).</summary>
    [HttpPut("load")]
    [Authorize(Policy = SecurityPolicies.AdminManagement)]
    public async Task<IActionResult> SetLoad(SetOfferFeeRequest request, CancellationToken cancellationToken) =>
        request.AmountPaise is null ? BadRequest(new { success = false, message = "Give the fee in paise", errors = Array.Empty<string>() }) : Respond(await fees.SetLoadAsync(request.AmountPaise.Value, cancellationToken));

    /// <summary>Sets one store's own fee, or removes it (null) so the default applies.</summary>
    [HttpPut("stores/{storeId:guid}")]
    [Authorize(Policy = SecurityPolicies.AdminManagement)]
    public async Task<IActionResult> SetStore(Guid storeId, SetOfferFeeRequest request, CancellationToken cancellationToken) => Respond(await fees.SetStoreAsync(storeId, request.AmountPaise, cancellationToken));

    /// <summary>What an offer would cost the person asking (their store's fee; nothing for an administrator), and whether online payment is on.</summary>
    [HttpGet("mine")]
    [Authorize(Policy = SecurityPolicies.OfferManagement)]
    public async Task<IActionResult> Mine(CancellationToken cancellationToken) => Respond(await fees.GetMineAsync(cancellationToken));

    private IActionResult Respond<T>(CampaignOperationResult<T> result) => result.Status switch
    {
        CampaignOperationStatus.Succeeded => Ok(new { success = true, data = result.Value }),
        CampaignOperationStatus.InvalidRequest => BadRequest(new { success = false, message = result.Message, errors = Array.Empty<string>() }),
        CampaignOperationStatus.Unauthorized => Forbid(),
        CampaignOperationStatus.NotFound => NotFound(new { success = false, message = result.Message, errors = Array.Empty<string>() }),
        _ => StatusCode(StatusCodes.Status500InternalServerError)
    };
}
