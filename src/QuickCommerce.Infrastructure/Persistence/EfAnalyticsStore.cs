using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using QuickCommerce.Application.Interfaces;
using QuickCommerce.Application.Services;
using QuickCommerce.Domain;

namespace QuickCommerce.Infrastructure.Persistence;

public sealed class AnalyticsEventConfiguration : IEntityTypeConfiguration<AnalyticsEvent>
{
    public void Configure(EntityTypeBuilder<AnalyticsEvent> builder)
    {
        builder.HasKey(item => item.Id);
        builder.Property(item => item.Id).ValueGeneratedOnAdd();
        builder.Property(item => item.Name).HasMaxLength(24).IsUnicode(false).IsRequired();
        builder.Property(item => item.SessionId).HasMaxLength(64).IsUnicode(false).IsRequired();
        builder.Property(item => item.Detail).HasMaxLength(AnalyticsRules.DetailMax).IsUnicode(false);
        builder.Property(item => item.OccurredAt).HasColumnType("datetime2");
        builder.HasIndex(item => new { item.Name, item.OccurredAt }).HasDatabaseName("IX_AnalyticsEvents_Name_OccurredAt");
        builder.HasIndex(item => item.OccurredAt).HasDatabaseName("IX_AnalyticsEvents_OccurredAt");
    }
}

public sealed class AnalyticsDailyConfiguration : IEntityTypeConfiguration<AnalyticsDaily>
{
    public void Configure(EntityTypeBuilder<AnalyticsDaily> builder)
    {
        builder.HasKey(item => new { item.Day, item.Name, item.StoreKey });
        builder.Property(item => item.Day).HasColumnType("date");
        builder.Property(item => item.Name).HasMaxLength(24).IsUnicode(false).IsRequired();
        builder.ToTable(table => table.HasCheckConstraint("CK_AnalyticsDaily_Counts", "[Events] >= 0 AND [Sessions] >= 0 AND [Sessions] <= [Events]"));
    }
}

/// <summary>Anonymous customer steps on SQL Server: written in small batches, read as totals, rolled up daily and purged after 90 days.</summary>
public sealed class EfAnalyticsStore(QuickCommerceDbContext db) : IAnalyticsStore
{
    public async Task AddAsync(IReadOnlyList<AnalyticsEvent> events, CancellationToken cancellationToken = default)
    {
        db.AnalyticsEvents.AddRange(events);
        await db.SaveChangesAsync(cancellationToken);
        db.ChangeTracker.Clear();
    }

    public async Task<IReadOnlyDictionary<string, StepTotal>> GetRawTotalsAsync(Guid? storeId, DateTime fromUtc, DateTime toUtc, CancellationToken cancellationToken = default) =>
        (await db.AnalyticsEvents.AsNoTracking()
            .Where(item => item.OccurredAt >= fromUtc && item.OccurredAt < toUtc && (storeId == null || item.StoreId == storeId))
            .GroupBy(item => item.Name)
            .Select(group => new { group.Key, Events = group.Count(), Sessions = group.Select(item => item.SessionId).Distinct().Count() })
            .ToListAsync(cancellationToken))
        .ToDictionary(item => item.Key, item => new StepTotal(item.Key, item.Events, item.Sessions));

    public async Task<IReadOnlyDictionary<string, StepTotal>> GetDailyTotalsAsync(Guid? storeId, DateOnly from, DateOnly to, CancellationToken cancellationToken = default)
    {
        // The row with no store is the total of every store; a store's own rows are only its events.
        var key = storeId ?? Guid.Empty;
        return (await db.AnalyticsDaily.AsNoTracking()
                .Where(item => item.Day >= from && item.Day <= to && item.StoreKey == key)
                .GroupBy(item => item.Name)
                .Select(group => new { group.Key, Events = group.Sum(item => item.Events), Sessions = group.Sum(item => item.Sessions) })
                .ToListAsync(cancellationToken))
            .ToDictionary(item => item.Key, item => new StepTotal(item.Key, item.Events, item.Sessions));
    }

    public async Task<IReadOnlyList<DetailCount>> GetTopDetailsAsync(string name, Guid? storeId, DateTime fromUtc, DateTime toUtc, int take, CancellationToken cancellationToken = default) =>
        (await db.AnalyticsEvents.AsNoTracking()
            .Where(item => item.Name == name && item.Detail != null && item.OccurredAt >= fromUtc && item.OccurredAt < toUtc && (storeId == null || item.StoreId == storeId))
            .GroupBy(item => item.Detail!)
            .Select(group => new { Detail = group.Key, Count = group.Count() })
            .OrderByDescending(item => item.Count).ThenBy(item => item.Detail)
            .Take(take)
            .ToListAsync(cancellationToken))
        .Select(item => new DetailCount(item.Detail, item.Count)).ToArray();

    public async Task<IReadOnlyList<DayCount>> GetSessionsPerDayAsync(Guid? storeId, DateOnly from, DateOnly to, DateTime rawFromUtc, CancellationToken cancellationToken = default)
    {
        var days = new Dictionary<DateOnly, int>();

        // Days already purged from the raw events: the sessions that opened the app that day.
        var rawFromDay = AnalyticsRules.DayOf(rawFromUtc);
        if (from < rawFromDay)
        {
            var key = storeId ?? Guid.Empty;
            var oldTo = to < rawFromDay.AddDays(-1) ? to : rawFromDay.AddDays(-1);
            foreach (var row in await db.AnalyticsDaily.AsNoTracking().Where(item => item.Name == AnalyticsNames.AppOpen && item.StoreKey == key && item.Day >= from && item.Day <= oldTo).ToListAsync(cancellationToken))
            {
                days[row.Day] = row.Sessions;
            }
        }

        var start = from > rawFromDay ? from : rawFromDay;
        if (start <= to)
        {
            var rawRows = await db.AnalyticsEvents.AsNoTracking()
                .Where(item => item.OccurredAt >= ReportMath.StartUtc(start) && item.OccurredAt < ReportMath.EndUtc(to) && (storeId == null || item.StoreId == storeId))
                .GroupBy(item => item.OccurredAt.AddMinutes(330).Date)
                .Select(group => new { Day = group.Key, Sessions = group.Select(item => item.SessionId).Distinct().Count() })
                .ToListAsync(cancellationToken);
            foreach (var row in rawRows)
            {
                days[DateOnly.FromDateTime(row.Day)] = row.Sessions;
            }
        }

        var list = new List<DayCount>();
        for (var day = from; day <= to; day = day.AddDays(1))
        {
            list.Add(new DayCount(day, days.GetValueOrDefault(day)));
        }

        return list;
    }

    public async Task<int> RollUpAsync(DateTime beforeUtc, CancellationToken cancellationToken = default)
    {
        // Every day still in the raw events up to the cut-off: recomputed in full each time, so repeating it (or running on two servers) writes the same numbers.
        var raw = await db.AnalyticsEvents.AsNoTracking().Where(item => item.OccurredAt < beforeUtc)
            .Select(item => new { item.Name, item.OccurredAt, item.SessionId, item.StoreId }).ToListAsync(cancellationToken);
        var rows = new Dictionary<(DateOnly Day, string Name, Guid Store), (int Events, HashSet<string> Sessions)>();
        void Add(DateOnly day, string name, Guid store, string session)
        {
            var key = (day, name, store);
            if (!rows.TryGetValue(key, out var entry))
            {
                entry = (0, []);
            }

            entry.Sessions.Add(session);
            rows[key] = (entry.Events + 1, entry.Sessions);
        }

        foreach (var item in raw)
        {
            var day = AnalyticsRules.DayOf(item.OccurredAt);
            Add(day, item.Name, Guid.Empty, item.SessionId);
            if (item.StoreId is { } store)
            {
                Add(day, item.Name, store, item.SessionId);
            }
        }

        foreach (var ((day, name, store), (events, sessions)) in rows)
        {
            var updated = await db.AnalyticsDaily.Where(row => row.Day == day && row.Name == name && row.StoreKey == store)
                .ExecuteUpdateAsync(set => set.SetProperty(row => row.Events, events).SetProperty(row => row.Sessions, sessions.Count), cancellationToken);
            if (updated == 0)
            {
                db.AnalyticsDaily.Add(new AnalyticsDaily { Day = day, Name = name, StoreKey = store, Events = events, Sessions = sessions.Count });
                try
                {
                    await db.SaveChangesAsync(cancellationToken);
                }
                catch (DbUpdateException)
                {
                    // Another server wrote the same row a moment ago; the next run keeps it right.
                }

                db.ChangeTracker.Clear();
            }
        }

        return rows.Count;
    }

    public Task<int> PurgeAsync(DateTime olderThanUtc, CancellationToken cancellationToken = default) =>
        db.AnalyticsEvents.Where(item => item.OccurredAt < olderThanUtc).ExecuteDeleteAsync(cancellationToken);
}
