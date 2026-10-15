using Microsoft.AspNetCore.Mvc;
using QuickCommerce.Application.Interfaces;

namespace QuickCommerce.Api.Controllers;

/// <summary>What is on offer at a store right now, for the customer's Home banner. Public, like the catalogue.</summary>
[ApiController]
[Route("api/catalog/offers")]
public sealed class OffersController(IOfferDiscountLookup offers, TimeProvider clock) : ControllerBase
{
    /// <summary>One offer, for the screen a notification or banner opens. 404 when it has not started, was cancelled or is unpaid.</summary>
    [HttpGet("{id:guid}")]
    public async Task<IActionResult> One(Guid id, CancellationToken cancellationToken)
    {
        var offer = await offers.GetOfferAsync(id, clock.GetUtcNow().UtcDateTime, cancellationToken);
        return offer is null
            ? NotFound(new { success = false, message = "Offer not found.", errors = Array.Empty<string>() })
            : Ok(new { success = true, data = offer });
    }

    [HttpGet]
    public async Task<IActionResult> Active([FromQuery] Guid storeId, CancellationToken cancellationToken)
    {
        if (storeId == Guid.Empty)
        {
            return BadRequest(new { success = false, message = "Choose a store.", errors = Array.Empty<string>() });
        }

        return Ok(new { success = true, data = await offers.GetActiveOffersAsync(storeId, clock.GetUtcNow().UtcDateTime, cancellationToken) });
    }
}
