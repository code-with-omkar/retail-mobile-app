namespace QuickCommerce.Api.Hosting;

public sealed class ShutdownLoggingHostedService(
    IHostApplicationLifetime lifetime,
    ILogger<ShutdownLoggingHostedService> logger) : IHostedService
{
    public Task StartAsync(CancellationToken cancellationToken)
    {
        lifetime.ApplicationStopping.Register(() => logger.LogInformation("Application shutdown requested"));
        lifetime.ApplicationStopped.Register(() => logger.LogInformation("Application stopped"));
        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken)
    {
        logger.LogInformation("Application shutdown is completing");
        return Task.CompletedTask;
    }
}