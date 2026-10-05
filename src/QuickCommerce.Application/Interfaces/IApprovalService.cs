using QuickCommerce.Application.DTOs;

namespace QuickCommerce.Application.Interfaces;

public interface IApprovalService
{
    Task<ApprovalOperationResult> CreateAsync(CreateApprovalRequest request, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<ApprovalRequestResponse>> GetMineAsync(CancellationToken cancellationToken = default);
    Task<IReadOnlyList<ApprovalRequestResponse>> GetPendingAsync(CancellationToken cancellationToken = default);
    Task<ApprovalOperationResult> ApproveAsync(Guid id, CancellationToken cancellationToken = default);
    Task<ApprovalOperationResult> RejectAsync(Guid id, string reason, CancellationToken cancellationToken = default);
}

public enum ApprovalOperationStatus { Succeeded, InvalidRequest, Unauthorized, NotFound, Conflict }
public sealed record ApprovalOperationResult(ApprovalOperationStatus Status, ApprovalRequestResponse? Request = null, string? Message = null);