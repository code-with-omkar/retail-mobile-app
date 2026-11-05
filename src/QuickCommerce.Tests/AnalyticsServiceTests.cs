using QuickCommerce.Application.DTOs;
using QuickCommerce.Application.Interfaces;
using QuickCommerce.Application.Services;
using QuickCommerce.Domain;
using Xunit;

namespace QuickCommerce.Tests;

/// <summary>Taking in anonymous steps and building the behaviour report, with the store faked (no database).</summary>
public sealed class AnalyticsServiceTests
{
    private static readonly DateTime Now = new(2026, 11, 2, 4, 30, 0, DateTimeKind.Utc); // 10:00 India, Mon 2 Nov 2026
    private static readonly Guid Org = Guid.NewGuid();
    private static readonly Guid MyStore = Guid.NewGuid();
    private const string Session = "3f2b8c1e9a7d4e56";

    private sealed class FakeNow : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => new(Now);
    }

    private sealed class FakeStore : IAnalyticsStore
    {
        public List<AnalyticsEvent> Added { get; } = [];
        public Dictionary<string, StepTotal> Raw { get; } = [];
        public Dictionary<string, StepTotal> Daily { get; } = [];
        public List<(Guid? Store, DateTime From, DateTime To)> RawAsked { get; } = [];
        public List<(Guid? Store, DateOnly From, DateOnly To)> DailyAsked { get; } = [];

        public Task AddAsync(IReadOnlyList<AnalyticsEvent> events, CancellationToken cancellationToken = default)
        {
            Added.AddRange(events);
            return Task.CompletedTask;
        }

        public Task<IReadOnlyDictionary<string, StepTotal>> GetRawTotalsAsync(Guid? storeId, DateTime fromUtc, DateTime toUtc, CancellationToken cancellationToken = default)
        {
            RawAsked.Add((storeId, fromUtc, toUtc));
            return Task.FromResult<IReadOnlyDictionary<string, StepTotal>>(Raw);
        }

        public Task<IReadOnlyDictionary<string, StepTotal>> GetDailyTotalsAsync(Guid? storeId, DateOnly from, DateOnly to, CancellationToken cancellationToken = default)
        {
            DailyAsked.Add((storeId, from, to));
            return Task.FromResult<IReadOnlyDictionary<string, StepTotal>>(Daily);
        }

        public Task<IReadOnlyList<DetailCount>> GetTopDetailsAsync(string name, Guid? storeId, DateTime fromUtc, DateTime toUtc, int take, CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<DetailCount>>(name == AnalyticsNames.Search ? [new DetailCount("tomato", 5)] : []);

        public Task<IReadOnlyList<DayCount>> GetSessionsPerDayAsync(Guid? storeId, DateOnly from, DateOnly to, DateTime rawFromUtc, CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<DayCount>>([new DayCount(from, 3)]);

        public Task<int> RollUpAsync(DateTime beforeUtc, CancellationToken cancellationToken = default) => Task.FromResult(0);
        public Task<int> PurgeAsync(DateTime olderThanUtc, CancellationToken cancellationToken = default) => Task.FromResult(0);
    }

    private sealed class Who(UserContext? context) : ICurrentUserContextResolver
    {
        public Task<UserContext?> ResolveAsync(CancellationToken cancellationToken = default) => Task.FromResult(context);
    }

    private static (AnalyticsService Service, FakeStore Store) Make(UserContext? who = null)
    {
        var store = new FakeStore();
        return (new AnalyticsService(store, new Who(who), new FakeNow()), store);
    }

    private static AnalyticsEventDto Step(string name, string? detail = null, Guid? store = null) => new(name, detail, store);

    [Fact]
    public async Task A_good_batch_is_stored_with_the_servers_time_and_the_session_and_cleaned_details()
    {
        var (service, store) = Make();

        var result = await service.RecordAsync(new AnalyticsBatchRequest(Session, [Step("search", "  Fresh MILK "), Step("add_to_cart", null, MyStore), Step("order_placed", "call me 9876543210")]));

        Assert.Equal(AnalyticsStatus.Succeeded, result.Status);
        Assert.Equal(3, result.Value);
        Assert.All(store.Added, item => { Assert.Equal(Session, item.SessionId); Assert.Equal(Now, item.OccurredAt); });
        Assert.Equal("fresh milk", store.Added[0].Detail);
        Assert.Equal(MyStore, store.Added[1].StoreId);
        Assert.Null(store.Added[2].Detail); // looks like a phone number: dropped
    }

    [Fact]
    public async Task An_empty_store_id_means_no_store()
    {
        var (service, store) = Make();
        await service.RecordAsync(new AnalyticsBatchRequest(Session, [Step("app_open", null, Guid.Empty)]));
        Assert.Null(store.Added.Single().StoreId);
    }

    [Theory]
    [InlineData("")]
    [InlineData("short")]
    [InlineData("bad session id with spaces")]
    public async Task A_bad_session_id_is_refused_and_nothing_is_stored(string session)
    {
        var (service, store) = Make();
        var result = await service.RecordAsync(new AnalyticsBatchRequest(session, [Step("app_open")]));
        Assert.Equal(AnalyticsStatus.Invalid, result.Status);
        Assert.Empty(store.Added);
    }

    [Fact]
    public async Task An_empty_batch_or_one_over_25_events_is_refused()
    {
        var (service, store) = Make();
        Assert.Equal(AnalyticsStatus.Invalid, (await service.RecordAsync(new AnalyticsBatchRequest(Session, []))).Status);
        Assert.Equal(AnalyticsStatus.Invalid, (await service.RecordAsync(new AnalyticsBatchRequest(Session, null!))).Status);
        Assert.Equal(AnalyticsStatus.Invalid, (await service.RecordAsync(new AnalyticsBatchRequest(Session, Enumerable.Repeat(Step("app_open"), 26).ToArray()))).Status);
        Assert.Equal(AnalyticsStatus.Succeeded, (await service.RecordAsync(new AnalyticsBatchRequest(Session, Enumerable.Repeat(Step("app_open"), 25).ToArray()))).Status);
        Assert.Equal(25, store.Added.Count);
    }

    [Fact]
    public async Task One_unknown_name_refuses_the_whole_batch_so_nothing_is_half_stored()
    {
        var (service, store) = Make();
        var result = await service.RecordAsync(new AnalyticsBatchRequest(Session, [Step("app_open"), Step("drop_table")]));
        Assert.Equal(AnalyticsStatus.Invalid, result.Status);
        Assert.Empty(store.Added);
    }

    [Fact]
    public async Task The_report_is_for_store_staff_and_administrators_only()
    {
        var customer = new UserContext(Guid.NewGuid(), Org, null, Role.Customer);
        Assert.Equal(AnalyticsStatus.Forbidden, (await Make(customer).Service.ReportAsync(new DateOnly(2026, 11, 1), new DateOnly(2026, 11, 2), null)).Status);
        Assert.Equal(AnalyticsStatus.Forbidden, (await Make(null).Service.ReportAsync(new DateOnly(2026, 11, 1), new DateOnly(2026, 11, 2), null)).Status);
    }

    [Fact]
    public async Task Store_staff_see_their_own_store_whatever_store_they_ask_for_and_staff_without_one_are_refused()
    {
        var (service, store) = Make(new UserContext(Guid.NewGuid(), Org, MyStore, Role.StoreStaff));

        var result = await service.ReportAsync(new DateOnly(2026, 11, 1), new DateOnly(2026, 11, 2), Guid.NewGuid());

        Assert.Equal(AnalyticsStatus.Succeeded, result.Status);
        Assert.Equal(MyStore, result.Value!.StoreId);
        Assert.All(store.RawAsked, ask => Assert.Equal(MyStore, ask.Store));
        Assert.Equal(AnalyticsStatus.Forbidden, (await Make(new UserContext(Guid.NewGuid(), Org, null, Role.StoreStaff)).Service.ReportAsync(new DateOnly(2026, 11, 1), new DateOnly(2026, 11, 2), null)).Status);
    }

    [Fact]
    public async Task An_administrator_sees_all_stores_or_the_one_they_name()
    {
        var (service, store) = Make(new UserContext(Guid.NewGuid(), Org, null, Role.Admin));
        var all = await service.ReportAsync(new DateOnly(2026, 11, 1), new DateOnly(2026, 11, 2), null);
        var one = await service.ReportAsync(new DateOnly(2026, 11, 1), new DateOnly(2026, 11, 2), MyStore);

        Assert.Null(all.Value!.StoreId);
        Assert.Equal(MyStore, one.Value!.StoreId);
        Assert.Equal([null, MyStore], store.RawAsked.Select(ask => ask.Store));
    }

    [Fact]
    public async Task A_bad_range_is_refused()
    {
        var (service, _) = Make(new UserContext(Guid.NewGuid(), Org, null, Role.Admin));
        Assert.Equal(AnalyticsStatus.Invalid, (await service.ReportAsync(new DateOnly(2026, 11, 5), new DateOnly(2026, 11, 2), null)).Status);
    }

    [Fact]
    public async Task The_report_has_the_funnel_every_step_a_top_search_and_a_row_per_day()
    {
        var (service, store) = Make(new UserContext(Guid.NewGuid(), Org, null, Role.Admin));
        store.Raw[AnalyticsNames.AppOpen] = new StepTotal(AnalyticsNames.AppOpen, 30, 20);
        store.Raw[AnalyticsNames.OrderPlaced] = new StepTotal(AnalyticsNames.OrderPlaced, 5, 4);

        var report = (await service.ReportAsync(new DateOnly(2026, 11, 1), new DateOnly(2026, 11, 2), null)).Value!;

        Assert.Equal(5, report.Funnel.Count);
        Assert.Equal(20, report.Funnel[0].Sessions);
        Assert.Equal(20, report.Funnel[4].PercentOfFirst); // 4 of 20 sessions
        Assert.Equal(AnalyticsNames.All.Length, report.Steps.Count);
        Assert.Equal(0, report.Steps.Single(step => step.Name == AnalyticsNames.Search).Sessions); // listed even with nothing
        Assert.Equal("tomato", report.TopSearches.Single().Detail);
        Assert.Single(report.DailySessions);
    }

    [Fact]
    public async Task A_range_inside_the_last_90_days_reads_only_the_raw_events()
    {
        var (service, store) = Make(new UserContext(Guid.NewGuid(), Org, null, Role.Admin));
        await service.ReportAsync(new DateOnly(2026, 10, 1), new DateOnly(2026, 11, 2), null);
        Assert.Single(store.RawAsked);
        Assert.Empty(store.DailyAsked);
    }

    [Fact]
    public async Task A_range_reaching_back_past_90_days_reads_the_older_days_from_the_daily_totals_and_adds_them_up()
    {
        var (service, store) = Make(new UserContext(Guid.NewGuid(), Org, null, Role.Admin));
        store.Raw[AnalyticsNames.AppOpen] = new StepTotal(AnalyticsNames.AppOpen, 10, 8);
        store.Daily[AnalyticsNames.AppOpen] = new StepTotal(AnalyticsNames.AppOpen, 100, 80);

        var report = (await service.ReportAsync(new DateOnly(2026, 8, 3), new DateOnly(2026, 11, 2), null)).Value!;

        // 90 days back from 2 Nov 2026 starts on 5 Aug: raw covers 5 Aug to 2 Nov, the daily totals cover 3 and 4 Aug (the longest range is 92 days).
        Assert.Equal(new DateOnly(2026, 8, 4), store.DailyAsked.Single().To);
        Assert.Equal(new DateOnly(2026, 8, 3), store.DailyAsked.Single().From);
        Assert.Equal(88, report.Steps.Single(step => step.Name == AnalyticsNames.AppOpen).Sessions);
        Assert.Equal(110, report.Steps.Single(step => step.Name == AnalyticsNames.AppOpen).Events);
    }

    [Fact]
    public async Task A_range_entirely_older_than_90_days_reads_only_the_daily_totals()
    {
        var (service, store) = Make(new UserContext(Guid.NewGuid(), Org, null, Role.Admin));
        await service.ReportAsync(new DateOnly(2026, 5, 1), new DateOnly(2026, 5, 20), null);
        Assert.Empty(store.RawAsked);
        Assert.Equal((new DateOnly(2026, 5, 1), new DateOnly(2026, 5, 20)), (store.DailyAsked.Single().From, store.DailyAsked.Single().To));
    }
}
