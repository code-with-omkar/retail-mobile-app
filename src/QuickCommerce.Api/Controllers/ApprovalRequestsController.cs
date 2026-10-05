using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using QuickCommerce.Application.DTOs;
using QuickCommerce.Application.Interfaces;

namespace QuickCommerce.Api.Controllers;

[ApiController]
[Route("api/approval-requests")]
[Authorize]
public sealed class ApprovalRequestsController(IApprovalService service) : ControllerBase
{
    [HttpPost]
    public async Task<IActionResult> Create(CreateApprovalRequest request, CancellationToken cancellationToken) => ToResponse(await service.CreateAsync(request, cancellationToken));

    [HttpGet("mine")]
    public async Task<IActionResult> Mine(CancellationToken cancellationToken) => Ok(new { success = true, data = await service.GetMineAsync(cancellationToken) });

    [HttpGet("pending")]
    public async Task<IActionResult> Pending(CancellationToken cancellationToken) => Ok(new { success = true, data = await service.GetPendingAsync(cancellationToken) });

    [HttpPost("{id:guid}/approve")]
    public async Task<IActionResult> Approve(Guid id, CancellationToken cancellationToken) => ToResponse(await service.ApproveAsync(id, cancellationToken));

    [HttpPost("{id:guid}/reject")]
    public async Task<IActionResult> Reject(Guid id, RejectApprovalRequest request, CancellationToken cancellationToken) => ToResponse(await service.RejectAsync(id, request.RejectionReason, cancellationToken));

    private IActionResult ToResponse(ApprovalOperationResult result) => result.Status switch
    {
        ApprovalOperationStatus.Succeeded => Ok(new { success = true, data = result.Request }),
        ApprovalOperationStatus.InvalidRequest => BadRequest(Failure(result.Message!)),
        ApprovalOperationStatus.Unauthorized => Forbid(),
        ApprovalOperationStatus.NotFound => NotFound(Failure(result.Message!)),
        ApprovalOperationStatus.Conflict => Conflict(Failure(result.Message!)),
        _ => StatusCode(StatusCodes.Status500InternalServerError)
    };

    private static object Failure(string message) => new { success = false, message, errors = Array.Empty<string>() };
}