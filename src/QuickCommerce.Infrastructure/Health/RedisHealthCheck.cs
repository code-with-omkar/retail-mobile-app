using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace QuickCommerce.Infrastructure.Health;

public sealed class RedisHealthCheck(IConfiguration configuration, IServiceProvider services) : IHealthCheck
{
    public async Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(configuration["Caching:RedisConnectionString"]))
        {
            return HealthCheckResult.Healthy("Redis is optional and disabled");
        }

        var cache = services.GetService<IDistributedCache>();
        if (cache is null)
        {
            return HealthCheckResult.Unhealthy("Redis is configured but not registered");
        }

        try
        {
            await cache.GetAsync("quickcommerce:health:probe", cancellationToken);
            return HealthCheckResult.Healthy();
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            return HealthCheckResult.Unhealthy("Redis is unavailable", exception);
        }
    }
}