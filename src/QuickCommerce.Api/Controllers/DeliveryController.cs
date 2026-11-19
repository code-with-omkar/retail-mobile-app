using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using QuickCommerce.Api.Security;
using QuickCommerce.Application.Interfaces;

namespace QuickCommerce.Api.Controllers;

/// <summary>Giving ready orders to delivery partners (store staff and administrators), and the partner's own deliveries.</summary>
[ApiController]
public sealed class DeliveryController(IDeliveryService delivery) : ControllerBase
{
    [HttpGet("api/delivery/partners")]
    [Authorize(Policy = SecurityPolicies.StoreOrderOperations)]
    public async Task<IActionResult> Partners([FromQuery] Guid? storeId, CancellationToken cancellationToken) => Respond(await delivery.ListPartnersAsync(storeId, cancellationToken));

    [HttpPost("api/operations/orders/{orderId:guid}/assign")]
    [Authorize(Policy = SecurityPolicies.StoreOrderOperations)]
    public async Task<IActionResult> Assign(Guid orderId, AssignDeliveryRequest request, CancellationToken cancellationToken) => Respond(await delivery.AssignAsync(orderId, request, cancellationToken));

    [HttpGet("api/delivery/mine")]
    [Authorize(Policy = SecurityPolicies.DeliveryPartner)]
    public async Task<IActionResult> Mine(CancellationToken cancellationToken) => Respond(await delivery.MineAsync(cancellationToken));

    [HttpPost("api/delivery/{orderId:guid}/pickup")]
    [Authorize(Policy = SecurityPolicies.DeliveryPartner)]
    public async Task<IActionResult> PickUp(Guid orderId, CancellationToken cancellationToken) => Respond(await delivery.PickUpAsync(orderId, cancellationToken));

    [HttpPost("api/delivery/{orderId:guid}/deliver")]
    [Authorize(Policy = SecurityPolicies.DeliveryPartner)]
    public async Task<IActionResult> Deliver(Guid orderId, DeliverRequest request, CancellationToken cancellationToken) => Respond(await delivery.DeliverAsync(orderId, request, cancellationToken));

    private IActionResult Respond<T>(DeliveryResult<T> result) => result.Status switch
    {
        DeliveryResultStatus.Succeeded => Ok(new { success = true, data = result.Value }),
        DeliveryResultStatus.Forbidden => Forbid(),
        DeliveryResultStatus.NotFound => NotFound(Failure(result.Message!)),
        DeliveryResultStatus.Invalid => BadRequest(Failure(result.Message!)),
        DeliveryResultStatus.WrongOtp => BadRequest(Failure(result.Message!, "WrongCode")),
        DeliveryResultStatus.Locked => StatusCode(StatusCodes.Status429TooManyRequests, Failure(result.Message!, "Locked")),
        _ => Conflict(Failure(result.Message!))
    };

    private static object Failure(string message, string? reason = null) => new { success = false, message, reason, errors = Array.Empty<string>() };
}
