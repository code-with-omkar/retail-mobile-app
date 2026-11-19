using QuickCommerce.Domain;

namespace QuickCommerce.Application.Interfaces;

public sealed record ProposeReplacementRequest(Guid ProductId, Guid? VariantId, Guid ReplacementProductId, Guid ReplacementVariantId, int Quantity);

/// <summary>A proposal as the store and the customer see it. Status: Pending, Accepted, Declined or Expired.</summary>
public sealed record ReplacementInfo(
    Guid Id,
    string Status,
    string OldName,
    string? OldLabel,
    int OldQuantity,
    decimal OldUnitPrice,
    string NewName,
    string? NewLabel,
    int NewQuantity,
    decimal NewUnitPrice,
    decimal PriceChange,
    DateTime ExpiresAt);

public enum ReplacementStatus { Succeeded, Forbidden, NotFound, Invalid, WrongState, Conflict }

public sealed record ReplacementResult<T>(ReplacementStatus Status, T? Value = default, string? Message = null, string? Reason = null);

public static class ReplacementReasons
{
    /// <summary>A dearer replacement cannot be charged to a payment already made online.</summary>
    public const string OnlinePaidDearer = "OnlinePaidDearer";
    public const string AlreadyOpen = "ReplacementAlreadyOpen";
    public const string Expired = "ReplacementExpired";
    public const string OutOfStock = "ReplacementOutOfStock";
}

public enum ReplacementOutcome { Applied, NotFound, Forbidden, WrongState, Invalid, Conflict }

public sealed record ReplacementCommit(ReplacementOutcome Outcome, string? Message = null, string? Reason = null, ReplacementProposal? Proposal = null, OrderAdjustment? Adjustment = null);

public interface IReplacementStore
{
    Task<ReplacementCommit> ProposeAsync(Guid orderId, Guid organizationId, Guid? storeId, Guid proposedBy, ProposeReplacementRequest request, DateTime now, TimeSpan window, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<ReplacementProposal>> ListAsync(Guid orderId, CancellationToken cancellationToken = default);

    /// <summary>The customer answers their own order's open proposal. Accepting swaps the line (and refunds a saving); declining removes the item.</summary>
    Task<ReplacementCommit> AnswerAsync(Guid orderId, Guid proposalId, Guid userId, bool accept, DateTime now, CancellationToken cancellationToken = default);

    /// <summary>Unanswered proposals past their time are treated as declined. Returns how many.</summary>
    Task<int> ExpireDueAsync(DateTime now, int max, CancellationToken cancellationToken = default);
}

public interface IReplacementService
{
    Task<ReplacementResult<ReplacementInfo>> ProposeAsync(Guid orderId, ProposeReplacementRequest request, CancellationToken cancellationToken = default);
    Task<ReplacementResult<IReadOnlyList<ReplacementInfo>>> ListAsync(Guid orderId, CancellationToken cancellationToken = default);
    Task<ReplacementResult<ReplacementInfo>> AnswerAsync(Guid orderId, Guid proposalId, bool accept, CancellationToken cancellationToken = default);
}
