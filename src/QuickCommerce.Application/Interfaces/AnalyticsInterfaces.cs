using QuickCommerce.Domain;

namespace QuickCommerce.Application.Interfaces;

/// <summary>One step from the app. The server sets the time; the app sends only what happened.</summary>
public sealed record AnalyticsEventDto(string Name, string? Detail = null, Guid? StoreId = null);

public sealed record AnalyticsBatchRequest(string SessionId, IReadOnlyList<AnalyticsEventDto> Events);

public sealed record FunnelStep(string Name, int Sessions, int PercentOfFirst, int PercentOfPrevious);

public sealed record StepTotal(string Name, int Events, int Sessions);

public sealed record DetailCount(string Detail, int Count);

public sealed record DayCount(DateOnly Day, int Sessions);

public sealed record BehaviourReport(
    DateOnly From,
    DateOnly To,
    Guid? StoreId,
    IReadOnlyList<FunnelStep> Funnel,
    IReadOnlyList<StepTotal> Steps,
    IReadOnlyList<DetailCount> TopSearches,
    IReadOnlyList<DetailCount> CouponRefusals,
    IReadOnlyList<DayCount> DailySessions);

public interface IAnalyticsStore
{
    Task AddAsync(IReadOnlyList<AnalyticsEvent> events, CancellationToken cancellationToken = default);

    /// <summary>Events and distinct sessions per step in the raw events from <paramref name="fromUtc"/> up to <paramref name="toUtc"/>; <paramref name="storeId"/> null means every store.</summary>
    Task<IReadOnlyDictionary<string, StepTotal>> GetRawTotalsAsync(Guid? storeId, DateTime fromUtc, DateTime toUtc, CancellationToken cancellationToken = default);

    /// <summary>The same from the daily totals, for days that have already been purged from the raw events.</summary>
    Task<IReadOnlyDictionary<string, StepTotal>> GetDailyTotalsAsync(Guid? storeId, DateOnly from, DateOnly to, CancellationToken cancellationToken = default);

    /// <summary>The most common details of one step (search terms, coupon refusal reasons) in the raw events.</summary>
    Task<IReadOnlyList<DetailCount>> GetTopDetailsAsync(string name, Guid? storeId, DateTime fromUtc, DateTime toUtc, int take, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<DayCount>> GetSessionsPerDayAsync(Guid? storeId, DateOnly from, DateOnly to, DateTime rawFromUtc, CancellationToken cancellationToken = default);

    /// <summary>Writes the daily totals for every day in the raw events before <paramref name="beforeUtc"/> (safe to repeat), and returns how many rows it wrote.</summary>
    Task<int> RollUpAsync(DateTime beforeUtc, CancellationToken cancellationToken = default);

    /// <summary>Deletes raw events older than <paramref name="olderThanUtc"/>, and returns how many.</summary>
    Task<int> PurgeAsync(DateTime olderThanUtc, CancellationToken cancellationToken = default);
}

public enum AnalyticsStatus { Succeeded, Invalid, Forbidden, NotFound }

public sealed record AnalyticsResult<T>(AnalyticsStatus Status, T? Value = default, string? Message = null);

public interface IAnalyticsService
{
    /// <summary>Anyone's phone, signed in or not: stores a small batch of steps for one session after checking every part of it.</summary>
    Task<AnalyticsResult<int>> RecordAsync(AnalyticsBatchRequest request, CancellationToken cancellationToken = default);

    /// <summary>Store staff for their own store, administrators for one store or all.</summary>
    Task<AnalyticsResult<BehaviourReport>> ReportAsync(DateOnly from, DateOnly to, Guid? storeId, CancellationToken cancellationToken = default);
}
