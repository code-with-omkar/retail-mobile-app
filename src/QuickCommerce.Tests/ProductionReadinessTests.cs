using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using QuickCommerce.Api.Middleware;
using QuickCommerce.Infrastructure.Health;
using Xunit;

namespace QuickCommerce.Tests;

public sealed class ProductionReadinessTests
{
    [Fact]
    public async Task Redis_health_is_healthy_when_redis_is_not_configured()
    {
        var configuration = new ConfigurationBuilder().Build();
        var check = new RedisHealthCheck(configuration, new ServiceCollection().BuildServiceProvider());

        var result = await check.CheckHealthAsync(new HealthCheckContext());

        Assert.Equal(HealthStatus.Healthy, result.Status);
    }

    [Fact]
    public async Task Sql_health_is_unhealthy_when_sql_connection_is_not_configured()
    {
        var configuration = new ConfigurationBuilder().Build();
        var check = new SqlServerHealthCheck(configuration);

        var result = await check.CheckHealthAsync(new HealthCheckContext());

        Assert.Equal(HealthStatus.Unhealthy, result.Status);
    }

    [Fact]
    public async Task Correlation_middleware_reuses_safe_header_and_generates_missing_header()
    {
        var firstContext = new DefaultHttpContext();
        firstContext.Request.Headers["X-Correlation-ID"] = "request-123";
        var middleware = new RequestCorrelationMiddleware(context => Task.CompletedTask);

        await middleware.InvokeAsync(firstContext);

        Assert.Equal("request-123", firstContext.TraceIdentifier);
        Assert.Equal("request-123", firstContext.Response.Headers["X-Correlation-ID"].ToString());

        var secondContext = new DefaultHttpContext();
        await middleware.InvokeAsync(secondContext);

        Assert.False(string.IsNullOrWhiteSpace(secondContext.TraceIdentifier));
        Assert.Equal(secondContext.TraceIdentifier, secondContext.Response.Headers["X-Correlation-ID"].ToString());
    }

    [Fact]
    public async Task Correlation_middleware_rejects_control_characters()
    {
        var context = new DefaultHttpContext();
        context.Request.Headers["X-Correlation-ID"] = "bad\r\nid";
        var middleware = new RequestCorrelationMiddleware(_ => Task.CompletedTask);

        await middleware.InvokeAsync(context);

        Assert.NotEqual("bad\r\nid", context.TraceIdentifier);
        Assert.DoesNotContain('\r', context.TraceIdentifier);
        Assert.DoesNotContain('\n', context.TraceIdentifier);
    }
}