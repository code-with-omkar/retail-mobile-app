using Sentry;
using Sentry.AspNetCore;

namespace QuickCommerce.Api.Hosting;

/// <summary>
/// Crash reporting with Sentry. It is off until <c>Sentry:Dsn</c> is set (user-secrets or the host's settings), and it never sends
/// who the person is: no user, no request body, no headers or cookies, no query string.
/// </summary>
public static class CrashReporting
{
    public static bool IsConfigured(IConfiguration configuration) => !string.IsNullOrWhiteSpace(configuration["Sentry:Dsn"]);

    public static void Configure(SentryAspNetCoreOptions options, IConfiguration configuration, IHostEnvironment environment)
    {
        options.Dsn = configuration["Sentry:Dsn"];
        options.Environment = environment.EnvironmentName;
        options.SendDefaultPii = false;
        options.MaxRequestBodySize = Sentry.Extensibility.RequestSize.None;
        options.TracesSampleRate = 0;
        options.SetBeforeSend(Scrub);
    }

    /// <summary>Removes anything that could identify a person or carry a secret from an event before it leaves the server.</summary>
    public static SentryEvent? Scrub(SentryEvent sentryEvent, SentryHint hint)
    {
        sentryEvent.User = new SentryUser();
        sentryEvent.Request.Data = null;
        sentryEvent.Request.Cookies = null;
        sentryEvent.Request.QueryString = null;
        sentryEvent.Request.Headers.Clear();
        sentryEvent.Request.Env.Clear();
        return sentryEvent;
    }
}
