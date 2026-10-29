using QuickCommerce.Application.Interfaces;
using QuickCommerce.Domain;

namespace QuickCommerce.Application.Services;

/// <summary>Replacement products. The store proposes (its own orders only); the customer answers (their own order only).</summary>
public sealed class ReplacementService(
    IReplacementStore store,
    ICurrentUserContextResolver contextResolver,
    TimeProvider? timeProvider = null) : IReplacementService
{
    private readonly TimeProvider clock = timeProvider ?? TimeProvider.System;

    private static ReplacementResult<T> Fail<T>(ReplacementCommit commit) => new(commit.Outcome switch
    {
        ReplacementOutcome.NotFound => ReplacementStatus.NotFound,
        ReplacementOutcome.Forbidden => ReplacementStatus.Forbidden,
        ReplacementOutcome.WrongState => ReplacementStatus.WrongState,
        ReplacementOutcome.Invalid => ReplacementStatus.Invalid,
        _ => ReplacementStatus.Conflict
    }, default, commit.Message, commit.Reason);

    public async Task<ReplacementResult<ReplacementInfo>> ProposeAsync(Guid orderId, ProposeReplacementRequest request, CancellationToken cancellationToken = default)
    {
        var context = await contextResolver.ResolveAsync(cancellationToken);
        if (context is null || context.Role is not (Role.StoreStaff or Role.Admin or Role.ApplicationAdmin) || (context.Role == Role.StoreStaff && context.StoreId is null))
        {
            return new(ReplacementStatus.Forbidden, Message: "Only store staff or an administrator can offer a replacement.");
        }

        var commit = await store.ProposeAsync(orderId, context.OrganizationId, context.Role == Role.StoreStaff ? context.StoreId : null, context.UserId, request, clock.GetUtcNow().UtcDateTime, ReplacementRules.Window, cancellationToken);
        return commit.Outcome == ReplacementOutcome.Applied ? new(ReplacementStatus.Succeeded, ReplacementRules.ToInfo(commit.Proposal!)) : Fail<ReplacementInfo>(commit);
    }

    public async Task<ReplacementResult<IReadOnlyList<ReplacementInfo>>> ListAsync(Guid orderId, CancellationToken cancellationToken = default)
    {
        var context = await contextResolver.ResolveAsync(cancellationToken);
        if (context is null || context.Role is not (Role.StoreStaff or Role.Admin or Role.ApplicationAdmin))
        {
            return new(ReplacementStatus.Forbidden, Message: "Only store staff or an administrator can see replacements.");
        }

        return new(ReplacementStatus.Succeeded, (await store.ListAsync(orderId, cancellationToken)).OrderByDescending(item => item.CreatedAt).Select(ReplacementRules.ToInfo).ToArray());
    }

    public async Task<ReplacementResult<ReplacementInfo>> AnswerAsync(Guid orderId, Guid proposalId, bool accept, CancellationToken cancellationToken = default)
    {
        var context = await contextResolver.ResolveAsync(cancellationToken);
        if (context is null || context.Role != Role.Customer)
        {
            return new(ReplacementStatus.Forbidden, Message: "Sign in to answer.");
        }

        var commit = await store.AnswerAsync(orderId, proposalId, context.UserId, accept, clock.GetUtcNow().UtcDateTime, cancellationToken);
        return commit.Outcome == ReplacementOutcome.Applied ? new(ReplacementStatus.Succeeded, ReplacementRules.ToInfo(commit.Proposal!)) : Fail<ReplacementInfo>(commit);
    }
}
