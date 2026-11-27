using Microsoft.AspNetCore.Http;
using QuickCommerce.Api.Middleware;
using QuickCommerce.Api.Security;
using QuickCommerce.Application.Services;
using QuickCommerce.Infrastructure.Security;
using Xunit;

namespace QuickCommerce.Tests;

/// <summary>Phase 7.8: the web-facing settings that keep the API from being used the wrong way.</summary>
public sealed class SecurityHardeningTests
{
    // ---------------- browsers ----------------

    [Fact]
    public void In_development_any_page_may_call_the_api()
    {
        var policy = ApiCors.Build(development: true, []);

        Assert.True(policy.AllowAnyOrigin);
        Assert.True(policy.AllowAnyHeader);
        Assert.True(policy.AllowAnyMethod);
    }

    [Fact]
    public void Elsewhere_only_the_listed_pages_may_call_it()
    {
        var policy = ApiCors.Build(development: false, ["https://portal.example.com", "https://admin.example.com"]);

        Assert.False(policy.AllowAnyOrigin);
        Assert.Equal(["https://portal.example.com", "https://admin.example.com"], policy.Origins);
    }

    [Fact]
    public void With_no_pages_listed_no_browser_page_may_call_it()
    {
        var policy = ApiCors.Build(development: false, []);

        Assert.False(policy.AllowAnyOrigin);
        Assert.Empty(policy.Origins);
    }

    [Fact]
    public void Origins_are_tidied_and_duplicates_dropped()
    {
        var origins = ApiCors.ParseOrigins([" https://portal.example.com/ ", "https://PORTAL.example.com", "", "  ", "http://localhost:4200"]);

        Assert.Equal(["https://portal.example.com", "http://localhost:4200"], origins);
        Assert.Empty(ApiCors.ParseOrigins(null));
    }

    [Theory]
    [InlineData("*")]
    [InlineData("https://*.example.com")]
    [InlineData("portal.example.com")]
    [InlineData("https://portal.example.com/app")]
    [InlineData("https://portal.example.com?x=1")]
    [InlineData("ftp://portal.example.com")]
    [InlineData("javascript:alert(1)")]
    public void An_entry_that_is_not_a_plain_origin_stops_the_api_from_starting(string entry) =>
        Assert.Throws<InvalidOperationException>(() => ApiCors.ParseOrigins([entry]));

    // ---------------- response headers ----------------

    private static async Task<IHeaderDictionary> HeadersFor(string path)
    {
        var context = new DefaultHttpContext();
        context.Request.Path = path;
        var middleware = new SecurityHeadersMiddleware(http => Task.CompletedTask);
        context.Response.Body = new MemoryStream();
        await middleware.InvokeAsync(context);
        return context.Response.Headers;
    }

    [Theory]
    [InlineData("/api/catalog/products")]
    [InlineData("/api/auth/login")]
    [InlineData("/health/live")]
    public async Task Every_answer_carries_the_basic_protective_headers(string path)
    {
        var headers = await HeadersFor(path);

        Assert.Equal("nosniff", headers["X-Content-Type-Options"]);
        Assert.Equal("no-referrer", headers["Referrer-Policy"]);
        Assert.Equal("DENY", headers["X-Frame-Options"]);
    }

    [Theory]
    [InlineData("/api/auth/login")]
    [InlineData("/api/auth/otp/verify")]
    [InlineData("/api/customer/orders")]
    [InlineData("/api/customer/profile/delete")]
    [InlineData("/api/account/phone/otp/verify")]
    [InlineData("/api/admin/catalog/products")]
    [InlineData("/api/delivery/mine")]
    [InlineData("/api/placement/bids")]
    [InlineData("/api/stock")]
    [InlineData("/API/AUTH/login")]
    public async Task Answers_about_a_person_or_their_account_are_never_kept_by_a_cache(string path) =>
        Assert.Equal("no-store", (await HeadersFor(path))["Cache-Control"]);

    [Theory]
    [InlineData("/api/catalog/products")]
    [InlineData("/api/authority")]
    [InlineData("/health/ready")]
    public async Task Public_answers_are_left_to_cache_as_they_were(string path) =>
        Assert.False((await HeadersFor(path)).ContainsKey("Cache-Control"));

    // ---------------- text-message senders ----------------

    [Fact]
    public void The_Msg91_sender_will_not_start_without_its_key_and_template()
    {
        Assert.Throws<InvalidOperationException>(() => new Msg91Settings().Validate());
        Assert.Throws<InvalidOperationException>(() => new Msg91Settings { AuthKey = "k" }.Validate());
        Assert.Throws<InvalidOperationException>(() => new Msg91Settings { TemplateId = "t" }.Validate());
        new Msg91Settings { AuthKey = "k", TemplateId = "t" }.Validate();
    }

    [Fact]
    public async Task A_refused_text_message_never_shows_the_number_or_the_code_in_the_error()
    {
        var handler = new RefusingHandler();
        var sender = new Msg91OtpSender(new HttpClient(handler), new Msg91Settings { AuthKey = "secret-key", TemplateId = "tpl" }, new OtpSettings(), Microsoft.Extensions.Logging.Abstractions.NullLogger<Msg91OtpSender>.Instance);

        var failure = await Assert.ThrowsAsync<InvalidOperationException>(() => sender.SendAsync("+919876543210", "123456"));

        Assert.DoesNotContain("9876543210", failure.Message);
        Assert.DoesNotContain("123456", failure.Message);
        Assert.DoesNotContain("secret-key", failure.Message);
        Assert.Equal("secret-key", handler.AuthKeyHeader);
        Assert.DoesNotContain("secret-key", handler.Url!);
        Assert.Contains("mobile=919876543210", handler.Url);
    }

    private sealed class RefusingHandler : HttpMessageHandler
    {
        public string? Url { get; private set; }
        public string? AuthKeyHeader { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Url = request.RequestUri!.ToString();
            AuthKeyHeader = request.Headers.TryGetValues("authkey", out var values) ? values.Single() : null;
            return Task.FromResult(new HttpResponseMessage(System.Net.HttpStatusCode.Unauthorized));
        }
    }
}
