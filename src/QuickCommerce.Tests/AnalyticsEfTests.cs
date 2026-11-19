using Microsoft.EntityFrameworkCore;
using QuickCommerce.Application.Interfaces;
using QuickCommerce.Application.Services;
using QuickCommerce.Domain;
using QuickCommerce.Infrastructure.Persistence;
using Xunit;
using static QuickCommerce.Tests.CheckoutEfIntegrationTests;

namespace QuickCommerce.Tests;

/// <summary>
/// Anonymous analytics on a real SQL Server (opt-in, like the other SQL tests). The events are dated in 2020 and rolled up and purged with
/// cut-offs in 2020, so nothing else in the database is touched, and everything added is removed again.
/// </summary>
[Collection("SqlServer")]
public sealed class AnalyticsEfTests : IAsyncLifetime
{
    private static readonly Guid StoreA = Guid.NewGuid();
    private static readonly Guid StoreB = Guid.NewGuid();
    private readonly string tag = Guid.NewGuid().ToString("N")[..12];

    private static QuickCommerceDbContext Db() => new(new DbContextOptionsBuilder<QuickCommerceDbContext>().UseSqlServer(ConnectionString!).Options);

    private string Session(string n) => $"{tag}-session-{n}-pad";

    private static DateTime At(int day, int hour, int minute = 0) => new(2020, 1, day, hour, minute, 0, DateTimeKind.Utc);

    private AnalyticsEvent Event(string name, string session, DateTime at, Guid? store = null, string? detail = null) =>
        new() { Name = name, SessionId = Session(session), OccurredAt = at, StoreId = store, Detail = detail };

    public async Task InitializeAsync()
    {
        if (ConnectionString is null) { return; }
        await using var db = Db();
        await db.Database.MigrateAsync();
    }

    public async Task DisposeAsync()
    {
        if (ConnectionString is null) { return; }
        await using var db = Db();
        await db.AnalyticsEvents.Where(item => item.SessionId.StartsWith(tag)).ExecuteDeleteAsync();
        await db.AnalyticsDaily.Where(item => item.Day.Year == 2020 && item.Day.Month == 1).ExecuteDeleteAsync();
    }

    [Fact]
    public async Task Events_are_added_and_counted_as_events_and_as_distinct_sessions_per_step()
    {
        if (ConnectionString is null) { return; }
        await using var db = Db();
        var store = new EfAnalyticsStore(db);
        await store.AddAsync([
            Event("app_open", "a", At(5, 10)), Event("app_open", "b", At(5, 11)),
            Event("add_to_cart", "a", At(5, 10, 5), StoreA), Event("add_to_cart", "a", At(5, 10, 6), StoreA), Event("add_to_cart", "b", At(5, 11, 5), StoreB),
        ]);

        await using var read = Db();
        var totals = await new EfAnalyticsStore(read).GetRawTotalsAsync(null, At(5, 0), At(6, 0));
        // Other data may exist in the database; this test's own steps are what is asserted, on a day nobody else uses.
        Assert.Equal(new StepTotal("app_open", 2, 2), totals["app_open"]);
        Assert.Equal(new StepTotal("add_to_cart", 3, 2), totals["add_to_cart"]); // 3 events from 2 sessions

        var one = await new EfAnalyticsStore(read).GetRawTotalsAsync(StoreA, At(5, 0), At(6, 0));
        Assert.Equal(new StepTotal("add_to_cart", 2, 1), one["add_to_cart"]);
        Assert.False(one.ContainsKey("app_open")); // no store, so not in a store's totals
        Assert.Empty(await new EfAnalyticsStore(read).GetRawTotalsAsync(null, At(6, 0), At(7, 0)));
    }

    [Fact]
    public async Task The_most_common_details_come_back_most_first_and_only_for_the_step_asked()
    {
        if (ConnectionString is null) { return; }
        await using var db = Db();
        await new EfAnalyticsStore(db).AddAsync([
            Event("search", "a", At(8, 10), null, "tomato"), Event("search", "b", At(8, 11), null, "tomato"), Event("search", "c", At(8, 12), null, "milk"),
            Event("search", "d", At(8, 13), null, null), Event("coupon_refused", "a", At(8, 14), null, "expired"),
        ]);

        await using var read = Db();
        var top = await new EfAnalyticsStore(read).GetTopDetailsAsync("search", null, At(8, 0), At(9, 0), 10);

        Assert.Equal([new DetailCount("tomato", 2), new DetailCount("milk", 1)], top);
        Assert.Equal([new DetailCount("expired", 1)], await new EfAnalyticsStore(read).GetTopDetailsAsync("coupon_refused", null, At(8, 0), At(9, 0), 10));
        Assert.Single(await new EfAnalyticsStore(read).GetTopDetailsAsync("search", null, At(8, 0), At(9, 0), 1));
    }

    [Fact]
    public async Task Sessions_are_counted_per_india_day_so_late_utc_evening_is_the_next_day()
    {
        if (ConnectionString is null) { return; }
        await using var db = Db();
        // 18:30 UTC on 9 Jan is 00:00 on 10 Jan in India.
        await new EfAnalyticsStore(db).AddAsync([
            Event("app_open", "a", At(9, 17)), Event("app_open", "b", At(9, 18, 29)), Event("app_open", "c", At(9, 18, 30)), Event("search", "c", At(9, 20), null, "x"),
        ]);

        await using var read = Db();
        var days = await new EfAnalyticsStore(read).GetSessionsPerDayAsync(null, new DateOnly(2020, 1, 9), new DateOnly(2020, 1, 10), At(1, 0));

        Assert.Equal([new DayCount(new DateOnly(2020, 1, 9), 2), new DayCount(new DateOnly(2020, 1, 10), 1)], days);
    }

    [Fact]
    public async Task Rolling_up_writes_a_row_for_all_stores_and_one_per_store_and_repeating_it_changes_nothing()
    {
        if (ConnectionString is null) { return; }
        await using var db = Db();
        await new EfAnalyticsStore(db).AddAsync([
            Event("add_to_cart", "a", At(12, 10), StoreA), Event("add_to_cart", "a", At(12, 11), StoreA), Event("add_to_cart", "b", At(12, 12), StoreB), Event("app_open", "a", At(12, 9)),
        ]);

        var written = await new EfAnalyticsStore(Db()).RollUpAsync(At(20, 0));
        var again = await new EfAnalyticsStore(Db()).RollUpAsync(At(20, 0));

        Assert.Equal(written, again);
        await using var read = Db();
        var day = new DateOnly(2020, 1, 12);
        var rows = await read.AnalyticsDaily.AsNoTracking().Where(row => row.Day == day).ToListAsync();
        Assert.Equal(3, rows.Single(row => row.Name == "add_to_cart" && row.StoreKey == Guid.Empty).Events);
        Assert.Equal(2, rows.Single(row => row.Name == "add_to_cart" && row.StoreKey == Guid.Empty).Sessions);
        Assert.Equal(2, rows.Single(row => row.Name == "add_to_cart" && row.StoreKey == StoreA).Events);
        Assert.Equal(1, rows.Single(row => row.Name == "add_to_cart" && row.StoreKey == StoreA).Sessions);
        Assert.Equal(1, rows.Single(row => row.Name == "app_open" && row.StoreKey == Guid.Empty).Events);
        Assert.DoesNotContain(rows, row => row.Name == "app_open" && row.StoreKey != Guid.Empty); // no store, no store row
        Assert.Equal(rows.Count, rows.Select(row => (row.Name, row.StoreKey)).Distinct().Count());
    }

    [Fact]
    public async Task Rolling_up_again_after_more_events_arrive_updates_the_totals()
    {
        if (ConnectionString is null) { return; }
        await using var db = Db();
        await new EfAnalyticsStore(db).AddAsync([Event("app_open", "a", At(13, 9))]);
        await new EfAnalyticsStore(Db()).RollUpAsync(At(20, 0));
        await new EfAnalyticsStore(Db()).AddAsync([Event("app_open", "b", At(13, 10))]);
        await new EfAnalyticsStore(Db()).RollUpAsync(At(20, 0));

        await using var read = Db();
        var row = await read.AnalyticsDaily.AsNoTracking().SingleAsync(item => item.Day == new DateOnly(2020, 1, 13) && item.Name == "app_open" && item.StoreKey == Guid.Empty);
        Assert.Equal((2, 2), (row.Events, row.Sessions));
    }

    [Fact]
    public async Task Purging_deletes_only_older_raw_events_and_the_daily_totals_stay_and_are_read_back()
    {
        if (ConnectionString is null) { return; }
        await using var db = Db();
        await new EfAnalyticsStore(db).AddAsync([Event("app_open", "a", At(14, 9)), Event("app_open", "b", At(14, 10)), Event("app_open", "c", At(25, 10))]);
        await new EfAnalyticsStore(Db()).RollUpAsync(At(20, 0));

        var purged = await new EfAnalyticsStore(Db()).PurgeAsync(At(20, 0));

        Assert.True(purged >= 2);
        await using var read = Db();
        var left = await read.AnalyticsEvents.AsNoTracking().Where(item => item.SessionId.StartsWith(tag)).Select(item => item.SessionId).ToListAsync();
        Assert.Equal([Session("c")], left); // the one after the cut-off stays
        var daily = await new EfAnalyticsStore(read).GetDailyTotalsAsync(null, new DateOnly(2020, 1, 14), new DateOnly(2020, 1, 14));
        Assert.Equal(new StepTotal("app_open", 2, 2), daily["app_open"]);
        var series = await new EfAnalyticsStore(read).GetSessionsPerDayAsync(null, new DateOnly(2020, 1, 14), new DateOnly(2020, 1, 14), DateTime.UtcNow.AddDays(-90));
        Assert.Equal(2, series.Single().Sessions); // an old day is answered from the daily totals
    }

    [Fact]
    public async Task The_database_refuses_daily_totals_with_more_sessions_than_events()
    {
        if (ConnectionString is null) { return; }
        await using var db = Db();
        db.AnalyticsDaily.Add(new AnalyticsDaily { Day = new DateOnly(2020, 1, 30), Name = "app_open", StoreKey = Guid.Empty, Events = 1, Sessions = 5 });
        await Assert.ThrowsAnyAsync<DbUpdateException>(() => db.SaveChangesAsync());
    }
}
