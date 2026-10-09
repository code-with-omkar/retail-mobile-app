using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using QuickCommerce.Api.Security;
using QuickCommerce.Application.Interfaces;
using QuickCommerce.Application.Services;

namespace QuickCommerce.Api.Controllers;

[ApiController]
public sealed class PaymentsController(ICustomerPaymentService payments, IPaymentWebhookService webhooks, PaymentSettings settings) : ControllerBase
{
    private const int MaxWebhookBytes = 256 * 1024;

    /// <summary>Which ways of paying are on, so the app shows only those. Anonymous.</summary>
    [HttpGet("api/payments/options")]
    public IActionResult Options() => Ok(new { success = true, data = new { cashOnDelivery = true, online = settings.Enabled, holdMinutes = settings.HoldMinutes } });

    /// <summary>Starts (or restarts, inside the hold) the payment of an online order and returns what the payment screen needs. No secrets.</summary>
    [HttpPost("api/customer/orders/{orderId:guid}/payment")]
    [Authorize(Policy = SecurityPolicies.Orders)]
    public async Task<IActionResult> Start(Guid orderId, CancellationToken cancellationToken) => Respond(await payments.StartAsync(orderId, cancellationToken));

    /// <summary>The app reports the result of the payment screen. Repeating it changes nothing.</summary>
    [HttpPost("api/customer/orders/{orderId:guid}/payment/confirm")]
    [Authorize(Policy = SecurityPolicies.Orders)]
    public async Task<IActionResult> Confirm(Guid orderId, ConfirmPaymentRequest request, CancellationToken cancellationToken) => Respond(await payments.ConfirmAsync(orderId, request, cancellationToken));

    /// <summary>
    /// The provider's notifications. Anonymous, because the provider is not a user, but every request must carry a valid signature over its raw body.
    /// 200 once handled (or deliberately ignored), 400 when the signature is wrong, 500 when it should be sent again.
    /// </summary>
    [HttpPost("api/payments/razorpay/webhook")]
    [AllowAnonymous]
    public async Task<IActionResult> Webhook(CancellationToken cancellationToken)
    {
        if (Request.ContentLength is > MaxWebhookBytes)
        {
            return StatusCode(StatusCodes.Status413PayloadTooLarge);
        }

        using var reader = new StreamReader(Request.Body);
        var body = await reader.ReadToEndAsync(cancellationToken);
        var result = await webhooks.HandleAsync(body, Request.Headers["X-Razorpay-Signature"].ToString(), Request.Headers["X-Razorpay-Event-Id"].ToString(), cancellationToken);
        return result switch
        {
            WebhookStatus.Accepted => Ok(new { success = true }),
            WebhookStatus.Rejected => BadRequest(new { success = false, message = "Invalid signature", errors = Array.Empty<string>() }),
            _ => StatusCode(StatusCodes.Status500InternalServerError, new { success = false, message = "Try again", errors = Array.Empty<string>() })
        };
    }

    private IActionResult Respond<T>(PaymentOperationResult<T> result) => result.Status switch
    {
        PaymentOperationStatus.Succeeded => Ok(new { success = true, data = result.Value }),
        PaymentOperationStatus.InvalidRequest => BadRequest(Failure(result.Message!)),
        PaymentOperationStatus.Unauthorized => Forbid(),
        PaymentOperationStatus.NotFound => NotFound(Failure(result.Message!)),
        PaymentOperationStatus.Conflict => Conflict(Failure(result.Message!, result.Reason)),
        _ => StatusCode(StatusCodes.Status500InternalServerError)
    };

    private static object Failure(string message, string? reason = null) => new { success = false, message, reason, errors = Array.Empty<string>() };
}
