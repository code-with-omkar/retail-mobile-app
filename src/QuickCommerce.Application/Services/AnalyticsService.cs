using QuickCommerce.Application.DTOs;
using QuickCommerce.Application.Interfaces;
using QuickCommerce.Domain;

namespace QuickCommerce.Application.Services;

/// <summary>Takes in the app's anonymous steps and builds the customer behaviour report.</summary>
public sealed class AnalyticsService(
    IAnalyticsStore store,
    ICurrentUserContextResolver userContext,
    TimeProvider? timeProvider = null) : IAnalyticsService
{
    private readonly TimeProvider clock = timeProvider ?? TimeProvider.System;

    public async Task<AnalyticsResult<int>> RecordAsync(AnalyticsBatchRequest request, CancellationToken cancellationToken = default)
    {
        if (!AnalyticsRules.IsSession(request?.SessionId))
        {
            return new(AnalyticsStatus.Invalid, Message: "The session id is not valid.");
        }

        if (request!.Events is null || request.Events.Count is 0 or > AnalyticsRules.MaxBatch)
        {
            return new(AnalyticsStatus.Invalid, Message: $"Send between 1 and {AnalyticsRules.MaxBatch} events.");
        }

        if (request.Events.FirstOrDefault(item => !AnalyticsRules.IsKnown(item?.Name)) is not null || request.Events.Any(item => item is null))
        {
            return new(AnalyticsStatus.Invalid, Message: "An event has an unknown name.");
        }

        var now = clock.GetUtcNow().UtcDateTime;
        var events = request.Events.Select(item => new AnalyticsEvent
        {
            Name = item.Name, OccurredAt = now, SessionId = request.SessionId, StoreId = item.StoreId == Guid.Empty ? null : item.StoreId, Detail = AnalyticsRules.CleanDetail(item.Detail),
        }).ToArray();
        await store.AddAsync(events, cancellationToken);
        return new(AnalyticsStatus.Succeeded, events.Length);
    }

    public async Task<AnalyticsResult<BehaviourReport>> ReportAsync(DateOnly from, DateOnly to, Guid? storeId, CancellationToken cancellationToken = default)
    {
        var context = await userContext.ResolveAsync(cancellationToken);
        if (context is null || context.Role is not (Role.StoreStaff or Role.Admin or Role.ApplicationAdmin))
        {
            return new(AnalyticsStatus.Forbidden, Message: "Customer behaviour is for store staff and administrators.");
        }

        if (ReportMath.Check(from, to) is { } problem)
        {
            return new(AnalyticsStatus.Invalid, Message: problem);
        }

        Guid? scope = storeId;
        if (context.Role == Role.StoreStaff)
        {
            // Store staff see a store of their own only: a manager any of theirs, otherwise the primary one.
            if (context.StoreId is not { } own)
            {
                return new(AnalyticsStatus.Forbidden, Message: "Store staff must be assigned to a store.");
            }

            scope = storeId is { } asked && context.OrderStoreIds.Contains(asked) ? asked : own;
        }

        var now = clock.GetUtcNow().UtcDateTime;
        var rawFromDay = AnalyticsRules.DayOf(now.AddDays(-(AnalyticsRules.RetentionDays - 1)));
        var rawStartDay = from < rawFromDay ? rawFromDay : from;
        var totals = new Dictionary<string, StepTotal>();

        // Days still in the raw events come from them; older days come from the daily totals written before they were purged.
        if (rawStartDay <= to)
        {
            Merge(totals, await store.GetRawTotalsAsync(scope, ReportMath.StartUtc(rawStartDay), ReportMath.EndUtc(to), cancellationToken));
        }

        if (from < rawFromDay)
        {
            var oldTo = to < rawFromDay.AddDays(-1) ? to : rawFromDay.AddDays(-1);
            Merge(totals, await store.GetDailyTotalsAsync(scope, from, oldTo, cancellationToken));
        }

        var rawFromUtc = ReportMath.StartUtc(rawStartDay);
        var rawToUtc = ReportMath.EndUtc(to);
        var report = new BehaviourReport(
            from, to, scope,
            AnalyticsRules.Funnel(totals.ToDictionary(item => item.Key, item => item.Value.Sessions)),
            AnalyticsNames.All.Select(name => totals.GetValueOrDefault(name) ?? new StepTotal(name, 0, 0)).ToArray(),
            rawStartDay <= to ? await store.GetTopDetailsAsync(AnalyticsNames.Search, scope, rawFromUtc, rawToUtc, AnalyticsRules.TopTerms, cancellationToken) : [],
            rawStartDay <= to ? await store.GetTopDetailsAsync(AnalyticsNames.CouponRefused, scope, rawFromUtc, rawToUtc, AnalyticsRules.TopTerms, cancellationToken) : [],
            await store.GetSessionsPerDayAsync(scope, from, to, ReportMath.StartUtc(rawFromDay), cancellationToken));
        return new(AnalyticsStatus.Succeeded, report);
    }

    private static void Merge(Dictionary<string, StepTotal> into, IReadOnlyDictionary<string, StepTotal> more)
    {
        foreach (var (name, add) in more)
        {
            into[name] = into.TryGetValue(name, out var have) ? new StepTotal(name, have.Events + add.Events, have.Sessions + add.Sessions) : add;
        }
    }
}
