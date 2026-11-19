using Microsoft.Extensions.Configuration;
using QuickCommerce.Api.Hosting;
using Sentry;
using Xunit;

namespace QuickCommerce.Tests;

public sealed class CrashReportingTests
{
    private static IConfiguration Config(string? dsn) => new ConfigurationBuilder()
        .AddInMemoryCollection(dsn is null ? [] : new Dictionary<string, string?> { ["Sentry:Dsn"] = dsn }).Build();

    [Theory]
    [InlineData(null, false)]
    [InlineData("", false)]
    [InlineData("   ", false)]
    [InlineData("https://key@example.test/1", true)]
    public void Crash_reporting_is_off_until_a_key_is_set(string? dsn, bool expected) =>
        Assert.Equal(expected, CrashReporting.IsConfigured(Config(dsn)));

    [Fact]
    public void An_event_leaves_the_server_without_the_person_the_body_headers_cookies_or_query()
    {
        var sentryEvent = new SentryEvent(new InvalidOperationException("boom"))
        {
            User = new SentryUser { Id = "u1", Email = "a@b.test", IpAddress = "1.2.3.4", Username = "asha" },
        };
        sentryEvent.Request.Data = "{\"phone\":\"9876543210\"}";
        sentryEvent.Request.Cookies = "session=abc";
        sentryEvent.Request.QueryString = "token=secret";
        sentryEvent.Request.Headers["Authorization"] = "Bearer secret";
        sentryEvent.Request.Env["REMOTE_ADDR"] = "1.2.3.4";

        var scrubbed = CrashReporting.Scrub(sentryEvent, new SentryHint())!;

        Assert.Null(scrubbed.User.Id);
        Assert.Null(scrubbed.User.Email);
        Assert.Null(scrubbed.User.IpAddress);
        Assert.Null(scrubbed.User.Username);
        Assert.Null(scrubbed.Request.Data);
        Assert.Null(scrubbed.Request.Cookies);
        Assert.Null(scrubbed.Request.QueryString);
        Assert.Empty(scrubbed.Request.Headers);
        Assert.Empty(scrubbed.Request.Env);
        Assert.IsType<InvalidOperationException>(scrubbed.Exception); // the crash itself is still reported
    }
}
