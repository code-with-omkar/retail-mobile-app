using QuickCommerce.Application.Interfaces;

namespace QuickCommerce.Api.Hosting;

/// <summary>
/// Every half minute, sends the offers whose start time has come. Safe on several servers at once (the store claims each campaign
/// with one conditional update), and a failure is logged and tried again on the next round.
/// </summary>
public sealed class CampaignBackgroundService(IServiceScopeFactory scopes, ILogger<CampaignBackgroundService> log) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(30));
        do
        {
            try
            {
                await using var scope = scopes.CreateAsyncScope();
                var sent = await scope.ServiceProvider.GetRequiredService<ICampaignStore>().PublishDueCampaignsAsync(DateTime.UtcNow, stoppingToken);
                if (sent > 0)
                {
                    log.LogInformation("Sent {Count} offer campaign(s)", sent);
                }
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                log.LogError(ex, "Sending offers failed; will try again");
            }
        }
        while (await WaitAsync(timer, stoppingToken));
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
