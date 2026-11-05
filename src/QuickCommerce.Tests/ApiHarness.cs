using Xunit;
using System.Net.Http.Headers;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.EntityFrameworkCore;
using QuickCommerce.Application.Interfaces;
using QuickCommerce.Infrastructure.Persistence;

namespace QuickCommerce.Tests;

/// <summary>
/// The real API in memory (every filter, policy and route) on the opt-in test SQL database, with the demo users the migrations seed.
/// A token is issued by the app's own session issuer, so the request is as a signed-in person would make it. Opt-in like the other SQL tests:
/// set QUICKCOMMERCE_TEST_CONNECTION_STRING to a DISPOSABLE database.
/// </summary>
public sealed class ApiHarness : WebApplicationFactory<Program>
{
    public static readonly Guid Admin = Guid.Parse("40000000-0000-0000-0000-000000000002");
    public static readonly Guid StoreStaff = Guid.Parse("40000000-0000-0000-0000-000000000003");
    public static readonly Guid Customer = Guid.Parse("40000000-0000-0000-0000-000000000004");
    public static readonly Guid DeliveryPartner = Guid.Parse("40000000-0000-0000-0000-000000000006");

    public static string? ConnectionString => CheckoutEfIntegrationTests.ConnectionString;

    private static readonly SemaphoreSlim MigrateLock = new(1, 1);
    private bool migrated;

    /// <summary>Brings the test database up to date once (an empty one has no tables, and the demo users come with the migrations).</summary>
    public async Task PrepareAsync()
    {
        await MigrateLock.WaitAsync();
        try
        {
            if (!migrated)
            {
                await using var db = new QuickCommerceDbContext(new DbContextOptionsBuilder<QuickCommerceDbContext>().UseSqlServer(ConnectionString!).Options);
                await db.Database.MigrateAsync();
                migrated = true;
            }
        }
        finally
        {
            MigrateLock.Release();
        }
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Development");
        builder.ConfigureAppConfiguration((_, config) => config.AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["ConnectionStrings:QuickCommerceDb"] = ConnectionString,
        }));
    }

    /// <summary>A client signed in as the given demo user, or not signed in at all for <c>null</c>.</summary>
    public async Task<HttpClient> ClientAsync(Guid? userId)
    {
        await PrepareAsync();
        var client = CreateClient();
        if (userId is { } id)
        {
            using var scope = Services.CreateScope();
            var session = await scope.ServiceProvider.GetRequiredService<ISessionIssuer>().IssueAsync(id, "127.0.0.1");
            Assert.True(session.Succeeded, session.Error);
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", session.Response!.AccessToken);
        }

        return client;
    }
}
