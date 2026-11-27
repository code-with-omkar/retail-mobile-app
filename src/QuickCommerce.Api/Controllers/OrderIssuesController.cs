using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using QuickCommerce.Api.Security;
using QuickCommerce.Application.Interfaces;

namespace QuickCommerce.Api.Controllers;

/// <summary>Problems customers report after delivery, and the store's decision.</summary>
[ApiController]
public sealed class OrderIssuesController(IOrderIssueService issues) : ControllerBase
{
    [HttpGet("api/customer/orders/{orderId:guid}/issue")]
    [Authorize(Policy = SecurityPolicies.Orders)]
    public async Task<IActionResult> Mine(Guid orderId, CancellationToken cancellationToken) => Respond(await issues.MineAsync(orderId, cancellationToken));

    [HttpPost("api/customer/orders/{orderId:guid}/issue")]
    [Authorize(Policy = SecurityPolicies.Orders)]
    public async Task<IActionResult> Report(Guid orderId, ReportIssueRequest request, CancellationToken cancellationToken) => Respond(await issues.ReportAsync(orderId, request, cancellationToken));

    [HttpGet("api/issues")]
    [Authorize(Policy = SecurityPolicies.StoreOrderOperations)]
    public async Task<IActionResult> List(CancellationToken cancellationToken) => Respond(await issues.ListAsync(cancellationToken));

    [HttpPost("api/issues/{id:guid}/decide")]
    [Authorize(Policy = SecurityPolicies.StoreOrderOperations)]
    public async Task<IActionResult> Decide(Guid id, DecideIssueRequest request, CancellationToken cancellationToken) => Respond(await issues.DecideAsync(id, request, cancellationToken));

    [HttpPost("api/issues/{id:guid}/cash-paid")]
    [Authorize(Policy = SecurityPolicies.StoreOrderOperations)]
    public async Task<IActionResult> CashPaid(Guid id, CancellationToken cancellationToken) => Respond(await issues.MarkCashPaidAsync(id, cancellationToken));

    private IActionResult Respond<T>(IssueResult<T> result)
    {
        var body = new { success = result.Status == IssueStatus.Succeeded, data = result.Value, message = result.Message, errors = Array.Empty<string>() };
        return result.Status switch
        {
            IssueStatus.Succeeded => Ok(body),
            IssueStatus.Forbidden => StatusCode(StatusCodes.Status403Forbidden, body),
            IssueStatus.NotFound => NotFound(body),
            IssueStatus.Conflict => Conflict(body),
            _ => BadRequest(body)
        };
    }
}
