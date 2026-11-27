using QuickCommerce.Application.Interfaces;
using QuickCommerce.Application.Services;

namespace QuickCommerce.Api.Hosting;

/// <summary>
/// Once a day (and shortly after start-up): writes the daily totals for every day still in the raw analytics events, then deletes raw events
/// older than 90 days. Writing the totals first means nothing is ever purged before it is counted. Safe to repeat and on several servers at once,
/// and a failure is logged and tried again the next day.
/// </summary>
public sealed class AnalyticsMaintenanceService(IServiceScopeFactory scopes, TimeProvider clock, ILogger<AnalyticsMaintenanceService> log) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        // A short wait lets start-up finish; then once a day.
        try
        {
            await Task.Delay(TimeSpan.FromMinutes(2), stoppingToken);
        }
        catch (OperationCanceledException)
        {
            return;
        }

        using var timer = new PeriodicTimer(TimeSpan.FromHours(24));
        do
        {
            await RunOnceAsync(stoppingToken);
        }
        while (await WaitAsync(timer, stoppingToken));
    }

    public async Task RunOnceAsync(CancellationToken stoppingToken)
    {
        try
        {
            await using var scope = scopes.CreateAsyncScope();
            var store = scope.ServiceProvider.GetRequiredService<IAnalyticsStore>();
            var now = clock.GetUtcNow().UtcDateTime;

            // Whole days that are finished (India time): everything before this morning's midnight.
            var today = AnalyticsRules.DayOf(now);
            var midnightUtc = today.ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc) - SlotRules.India;
            var written = await store.RollUpAsync(midnightUtc, stoppingToken);
            var purged = await store.PurgeAsync(now.AddDays(-AnalyticsRules.RetentionDays), stoppingToken);
            log.LogInformation("Analytics upkeep: wrote {Written} daily totals, deleted {Purged} old events", written, purged);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            log.LogError(ex, "Analytics upkeep failed; will try again tomorrow");
        }
    }

    private static async Task<bool> WaitAsync(PeriodicTimer timer, CancellationToken stoppingToken)
    {
        try
        {
            return await timer.WaitForNextTickAsync(stoppingToken);
        }
        catch (OperationCanceledException)
        {
            return false;
        }
    }
}
