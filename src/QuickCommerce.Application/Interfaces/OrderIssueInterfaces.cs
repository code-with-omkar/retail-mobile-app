using QuickCommerce.Domain;

namespace QuickCommerce.Application.Interfaces;

public sealed record IssueLineRequest(Guid ProductId, Guid? VariantId, int Quantity);

public sealed record ReportIssueRequest(string? Kind, IReadOnlyList<IssueLineRequest>? Items, string? Note = null);

/// <param name="Amount">Rupees to give back when approving; null means the whole claim.</param>
public sealed record DecideIssueRequest(bool Approve, decimal? Amount = null, string? Note = null);

public sealed record IssueLine(Guid ProductId, Guid? VariantId, string Name, string? Label, int Quantity, decimal Amount);

public sealed record IssueResponse(
    Guid Id,
    Guid OrderId,
    string OrderNumber,
    Guid StoreId,
    string Store,
    string Customer,
    string Kind,
    string? Note,
    string Status,
    decimal ClaimedAmount,
    decimal? ApprovedAmount,
    string? DecisionNote,
    string? RefundMethod,
    DateTime? CashPaidAt,
    DateTime CreatedAt,
    DateTime? DecidedAt,
    IReadOnlyList<IssueLine> Items,
    bool CanDecide = false);

/// <summary>What the customer's order screen needs: the report made (if any) and until when one can still be made.</summary>
public sealed record CustomerIssueResponse(IssueResponse? Issue, DateTime? ReportUntil);

public enum IssueStatus { Succeeded, Forbidden, NotFound, Invalid, Conflict }

public sealed record IssueResult<T>(IssueStatus Status, T? Value = default, string? Message = null);

/// <summary>An order with what a report about it needs.</summary>
public sealed record IssueOrder(Order Order, Guid OrganizationId, DateTime? DeliveredAt);

public sealed record IssueRow(OrderIssue Issue, string OrderNumber, string Store, string Customer);

public interface IOrderIssueStore
{
    Task<IssueOrder?> GetCustomerOrderAsync(Guid orderId, Guid userId, CancellationToken cancellationToken = default);
    Task<IssueRow?> GetByOrderAsync(Guid orderId, CancellationToken cancellationToken = default);
    Task<IssueRow?> GetAsync(Guid organizationId, Guid id, CancellationToken cancellationToken = default);

    /// <summary>Newest first, at most 500. <paramref name="storeIds"/> null means every store of the organisation.</summary>
    Task<IReadOnlyList<IssueRow>> ListAsync(Guid organizationId, IReadOnlyCollection<Guid>? storeIds, CancellationToken cancellationToken = default);

    /// <summary>False when the order already has a report.</summary>
    Task<bool> AddAsync(OrderIssue issue, CancellationToken cancellationToken = default);

    /// <summary>
    /// Saves the decision of an open report once, tells the customer, and for an online payment queues the refund (never more than what is
    /// left of the payment). False when it was no longer open.
    /// </summary>
    Task<bool> DecideAsync(Guid id, bool approve, decimal amount, string? note, Guid decidedBy, DateTime now, CancellationToken cancellationToken = default);

    /// <summary>False unless it is an approved cash refund not yet marked paid.</summary>
    Task<bool> MarkCashPaidAsync(Guid id, Guid paidBy, DateTime now, CancellationToken cancellationToken = default);
}

public interface IOrderIssueService
{
    Task<IssueResult<CustomerIssueResponse>> MineAsync(Guid orderId, CancellationToken cancellationToken = default);
    Task<IssueResult<IssueResponse>> ReportAsync(Guid orderId, ReportIssueRequest request, CancellationToken cancellationToken = default);
    Task<IssueResult<IReadOnlyList<IssueResponse>>> ListAsync(CancellationToken cancellationToken = default);
    Task<IssueResult<IssueResponse>> DecideAsync(Guid id, DecideIssueRequest request, CancellationToken cancellationToken = default);
    Task<IssueResult<IssueResponse>> MarkCashPaidAsync(Guid id, CancellationToken cancellationToken = default);
}
