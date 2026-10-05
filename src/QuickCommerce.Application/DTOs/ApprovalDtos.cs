using QuickCommerce.Domain;

namespace QuickCommerce.Application.DTOs;

public sealed record CreateApprovalRequest(string EntityType, Guid EntityId, string ApprovalType, Guid StoreId);
public sealed record RejectApprovalRequest(string RejectionReason);
public sealed record ApprovalRequestResponse(Guid Id, Guid OrganizationId, Guid StoreId, string StoreName, Guid RequestedByUserId, string RequestedBy, string EntityType, Guid EntityId, string ApprovalType, ApprovalStatus Status, Guid? ApprovedByUserId, DateTime? ApprovedAt, DateTime? RejectedAt, string? RejectionReason, DateTime CreatedAt);