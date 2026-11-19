using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using QuickCommerce.Api.Security;
using QuickCommerce.Application.Interfaces;

namespace QuickCommerce.Api.Controllers;

/// <summary>Replacement products: the store offers one for an item it cannot supply; the customer accepts or declines.</summary>
[ApiController]
public sealed class ReplacementsController(IReplacementService replacements) : ControllerBase
{
    [HttpPost("api/operations/orders/{orderId:guid}/replacements")]
    [Authorize(Policy = SecurityPolicies.StoreOrderOperations)]
    public async Task<IActionResult> Propose(Guid orderId, ProposeReplacementRequest request, CancellationToken cancellationToken) => Respond(await replacements.ProposeAsync(orderId, request, cancellationToken));

    [HttpGet("api/operations/orders/{orderId:guid}/replacements")]
    [Authorize(Policy = SecurityPolicies.StoreOrderOperations)]
    public async Task<IActionResult> List(Guid orderId, CancellationToken cancellationToken) => Respond(await replacements.ListAsync(orderId, cancellationToken));

    [HttpPost("api/customer/orders/{orderId:guid}/replacements/{proposalId:guid}/accept")]
    [Authorize(Policy = SecurityPolicies.Orders)]
    public async Task<IActionResult> Accept(Guid orderId, Guid proposalId, CancellationToken cancellationToken) => Respond(await replacements.AnswerAsync(orderId, proposalId, true, cancellationToken));

    [HttpPost("api/customer/orders/{orderId:guid}/replacements/{proposalId:guid}/decline")]
    [Authorize(Policy = SecurityPolicies.Orders)]
    public async Task<IActionResult> Decline(Guid orderId, Guid proposalId, CancellationToken cancellationToken) => Respond(await replacements.AnswerAsync(orderId, proposalId, false, cancellationToken));

    private IActionResult Respond<T>(ReplacementResult<T> result)
    {
        var body = new { success = result.Status == ReplacementStatus.Succeeded, data = result.Value, message = result.Message, reason = result.Reason, errors = Array.Empty<string>() };
        return result.Status switch
        {
            ReplacementStatus.Succeeded => Ok(body),
            ReplacementStatus.Forbidden => StatusCode(StatusCodes.Status403Forbidden, body),
            ReplacementStatus.NotFound => NotFound(body),
            ReplacementStatus.Invalid => BadRequest(body),
            _ => Conflict(body)
        };
    }
}
