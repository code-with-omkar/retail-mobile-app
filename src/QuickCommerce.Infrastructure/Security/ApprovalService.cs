using Microsoft.EntityFrameworkCore;
using QuickCommerce.Application.DTOs;
using QuickCommerce.Application.Interfaces;
using QuickCommerce.Domain;
using QuickCommerce.Infrastructure.Persistence;

namespace QuickCommerce.Infrastructure.Security;

public sealed class ApprovalService(QuickCommerceDbContext db, IAuthorizationScopeService scopeService) : IApprovalService
{
    public async Task<ApprovalOperationResult> CreateAsync(CreateApprovalRequest request, CancellationToken cancellationToken = default)
    {
        var scope = await scopeService.ResolveAsync(cancellationToken);
        if (scope is null || !scope.RoleCodes.Contains("StoreStaff") || !await IsCategoryAsync(scope.UserId, StaffCategory.StoreEmployee, cancellationToken) || !scope.CanAccessStore(request.StoreId))
        {
            return new(ApprovalOperationStatus.Unauthorized, Message: "Only an assigned StoreEmployee can create approval requests.");
        }

        if (!await db.Stores.AnyAsync(store => store.Id == request.StoreId && store.OrganizationId == scope.OrganizationId && store.IsActive, cancellationToken) || string.IsNullOrWhiteSpace(request.EntityType) || string.IsNullOrWhiteSpace(request.ApprovalType))
        {
            return new(ApprovalOperationStatus.InvalidRequest, Message: "The approval request is invalid.");
        }

        var approval = new ApprovalRequest { OrganizationId = scope.OrganizationId, StoreId = request.StoreId, RequestedByUserId = scope.UserId, EntityType = request.EntityType.Trim(), EntityId = request.EntityId, ApprovalType = request.ApprovalType.Trim(), CreatedBy = scope.UserId.ToString() };
        db.ApprovalRequests.Add(approval);
        await db.SaveChangesAsync(cancellationToken);
        return new(ApprovalOperationStatus.Succeeded, await MapAsync(approval.Id, cancellationToken));
    }

    public Task<IReadOnlyList<ApprovalRequestResponse>> GetMineAsync(CancellationToken cancellationToken = default) => GetForScopeAsync(false, cancellationToken);

    public Task<IReadOnlyList<ApprovalRequestResponse>> GetPendingAsync(CancellationToken cancellationToken = default) => GetForScopeAsync(true, cancellationToken);

    public Task<ApprovalOperationResult> ApproveAsync(Guid id, CancellationToken cancellationToken = default) => DecideAsync(id, true, null, cancellationToken);

    public Task<ApprovalOperationResult> RejectAsync(Guid id, string reason, CancellationToken cancellationToken = default) => DecideAsync(id, false, reason, cancellationToken);

    private async Task<IReadOnlyList<ApprovalRequestResponse>> GetForScopeAsync(bool pendingOnly, CancellationToken cancellationToken)
    {
        var scope = await scopeService.ResolveAsync(cancellationToken);
        if (scope is null) return [];
        var query = db.ApprovalRequests.AsNoTracking().Where(request => request.OrganizationId == scope.OrganizationId);
        if (pendingOnly)
        {
            if (!scope.IsApplicationAdmin && (!scope.RoleCodes.Contains("StoreStaff") || !await IsCategoryAsync(scope.UserId, StaffCategory.StoreManager, cancellationToken))) return [];
            query = query.Where(request => request.Status == ApprovalStatus.Pending && (scope.IsApplicationAdmin || scope.StoreIds.Contains(request.StoreId)));
        }
        else query = query.Where(request => request.RequestedByUserId == scope.UserId);
        return await query.Include(request => request.Store).Include(request => request.RequestedByUser).OrderByDescending(request => request.CreatedAt).Select(MapExpression()).ToListAsync(cancellationToken);
    }

    private async Task<ApprovalOperationResult> DecideAsync(Guid id, bool approve, string? reason, CancellationToken cancellationToken)
    {
        var scope = await scopeService.ResolveAsync(cancellationToken);
        if (scope is null || (!scope.IsApplicationAdmin && (!scope.RoleCodes.Contains("StoreStaff") || !await IsCategoryAsync(scope.UserId, StaffCategory.StoreManager, cancellationToken)))) return new(ApprovalOperationStatus.Unauthorized, Message: "Only a StoreManager can decide approval requests.");
        var request = await db.ApprovalRequests.SingleOrDefaultAsync(item => item.Id == id, cancellationToken);
        if (request is null || request.OrganizationId != scope.OrganizationId) return new(ApprovalOperationStatus.NotFound, Message: "Approval request not found.");
        if (!scope.IsApplicationAdmin && !scope.StoreIds.Contains(request.StoreId)) return new(ApprovalOperationStatus.Unauthorized, Message: "The request belongs to an unassigned store.");
        if (request.RequestedByUserId == scope.UserId) return new(ApprovalOperationStatus.Unauthorized, Message: "A requester cannot approve their own request.");
        if (request.Status != ApprovalStatus.Pending) return new(ApprovalOperationStatus.Conflict, Message: "The approval request is no longer pending.");
        if (!approve && string.IsNullOrWhiteSpace(reason)) return new(ApprovalOperationStatus.InvalidRequest, Message: "A rejection reason is required.");
        request.Status = approve ? ApprovalStatus.Approved : ApprovalStatus.Rejected;
        request.ApprovedByUserId = scope.UserId;
        request.ApprovedAt = approve ? DateTime.UtcNow : null;
        request.RejectedAt = approve ? null : DateTime.UtcNow;
        request.RejectionReason = approve ? null : reason!.Trim();
        request.UpdatedAt = DateTime.UtcNow;
        request.UpdatedBy = scope.UserId.ToString();
        await db.SaveChangesAsync(cancellationToken);
        return new(ApprovalOperationStatus.Succeeded, await MapAsync(request.Id, cancellationToken));
    }

    private Task<bool> IsCategoryAsync(Guid userId, StaffCategory category, CancellationToken cancellationToken) => db.Users.AsNoTracking().AnyAsync(user => user.Id == userId && user.StaffCategory == category && user.IsActive, cancellationToken);
    private async Task<ApprovalRequestResponse> MapAsync(Guid id, CancellationToken cancellationToken) => await db.ApprovalRequests.AsNoTracking().Where(request => request.Id == id).Include(request => request.Store).Include(request => request.RequestedByUser).Select(MapExpression()).SingleAsync(cancellationToken);
    private static System.Linq.Expressions.Expression<Func<ApprovalRequest, ApprovalRequestResponse>> MapExpression() => request => new ApprovalRequestResponse(request.Id, request.OrganizationId, request.StoreId, request.Store.Name, request.RequestedByUserId, request.RequestedByUser.DisplayName, request.EntityType, request.EntityId, request.ApprovalType, request.Status, request.ApprovedByUserId, request.ApprovedAt, request.RejectedAt, request.RejectionReason, request.CreatedAt);
}