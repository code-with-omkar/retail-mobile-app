using System.Net;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.AspNetCore.RateLimiting;

namespace QuickCommerce.Api.Security;

public static class RateLimitPolicies
{
    public const string Login = "auth-login";
    public const string Register = "auth-register";
    public const string ForgotPassword = "auth-forgot-password";
    public const string ResetPassword = "auth-reset-password";
    public const string ChangePassword = "auth-change-password";
}

/// <summary>Per-client-IP limits on the anonymous auth routes. Counters live in each server's memory, so with N instances the
/// effective limit is up to N times higher; per-account limits (reset codes) are kept in SQL and hold across instances.</summary>
public sealed class AuthRateLimitSettings
{
    public const string SectionName = "RateLimits:Auth";

    public int LoginPerMinute { get; set; } = 10;
    public int RegisterPerHour { get; set; } = 5;
    public int ForgotPasswordPerHour { get; set; } = 5;
    public int ResetPasswordPerHour { get; set; } = 10;
    public int ChangePasswordPerHour { get; set; } = 10;
}

public static class AuthRateLimiting
{
    public static IServiceCollection AddAuthRateLimiting(this IServiceCollection services, IConfiguration configuration)
    {
        var settings = configuration.GetSection(AuthRateLimitSettings.SectionName).Get<AuthRateLimitSettings>() ?? new AuthRateLimitSettings();
        services.AddRateLimiter(options =>
        {
            options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
            options.OnRejected = async (context, cancellationToken) =>
            {
                if (context.Lease.TryGetMetadata(MetadataName.RetryAfter, out var retryAfter))
                {
                    context.HttpContext.Response.Headers.RetryAfter = ((int)Math.Ceiling(retryAfter.TotalSeconds)).ToString();
                }

                context.HttpContext.Response.ContentType = "application/json";
                await context.HttpContext.Response.WriteAsJsonAsync(new { success = false, message = "Too many attempts. Please wait a moment and try again.", errors = Array.Empty<string>() }, cancellationToken);
            };
            Add(options, RateLimitPolicies.Login, settings.LoginPerMinute, TimeSpan.FromMinutes(1));
            Add(options, RateLimitPolicies.Register, settings.RegisterPerHour, TimeSpan.FromHours(1));
            Add(options, RateLimitPolicies.ForgotPassword, settings.ForgotPasswordPerHour, TimeSpan.FromHours(1));
            Add(options, RateLimitPolicies.ResetPassword, settings.ResetPasswordPerHour, TimeSpan.FromHours(1));
            Add(options, RateLimitPolicies.ChangePassword, settings.ChangePasswordPerHour, TimeSpan.FromHours(1));
        });
        return services;
    }

    private static void Add(RateLimiterOptions options, string name, int permits, TimeSpan window) =>
        options.AddPolicy(name, http => RateLimitPartition.GetFixedWindowLimiter(
            http.Connection.RemoteIpAddress?.ToString() ?? "unknown",
            _ => new FixedWindowRateLimiterOptions { PermitLimit = permits, Window = window, QueueLimit = 0 }));

    /// <summary>Honour X-Forwarded-For only from the configured trusted proxies (Proxy:KnownProxies). Without this, every
    /// client behind a load balancer shares the balancer's IP and one busy minute would block everyone.</summary>
    public static IServiceCollection AddTrustedProxies(this IServiceCollection services, IConfiguration configuration)
    {
        var proxies = KnownProxies(configuration);
        services.Configure<ForwardedHeadersOptions>(options =>
        {
            options.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto;
            options.ForwardLimit = 1;
            foreach (var proxy in proxies)
            {
                options.KnownProxies.Add(proxy);
            }
        });
        return services;
    }

    public static IReadOnlyList<IPAddress> KnownProxies(IConfiguration configuration) =>
        (configuration.GetSection("Proxy:KnownProxies").Get<string[]>() ?? []).Select(IPAddress.Parse).ToArray();
}
