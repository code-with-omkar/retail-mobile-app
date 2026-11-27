using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using QuickCommerce.Application.Interfaces;
using QuickCommerce.Application.Services;
using QuickCommerce.Infrastructure.Persistence;
using Xunit;

namespace QuickCommerce.Tests;

[Collection("SqlServer")]
public sealed class StoreAdminHttpTests : IClassFixture<ApiHarness>
{
    private readonly ApiHarness api;

    public StoreAdminHttpTests(ApiHarness api) => this.api = api;

    private static SaveStoreRequest Good(string name = "Test Store", string code = "tst1") => new(name, "1 Test Road", 19.04, 73.07, 6, code, "022 1234 5678");

    [Fact]
    public void A_store_needs_a_name_a_place_a_sensible_radius_and_a_code()
    {
        Assert.Null(StoreRules.Problem(Good()));
        Assert.NotNull(StoreRules.Problem(Good() with { Name = " " }));
        Assert.NotNull(StoreRules.Problem(Good() with { Latitude = 0, Longitude = 0 }));
        Assert.NotNull(StoreRules.Problem(Good() with { Longitude = 200 }));
        Assert.NotNull(StoreRules.Problem(Good() with { ServiceRadiusKm = 80 }));
        Assert.NotNull(StoreRules.Problem(Good() with { Code = "A B" }));
        Assert.NotNull(StoreRules.Problem(Good() with { PhoneNumber = "call me" }));
    }

    [Fact]
    public async Task An_administrator_adds_and_changes_a_store_and_nobody_else_can()
    {
        if (ApiHarness.ConnectionString is null) { return; }
        var code = "T" + Guid.NewGuid().ToString("N")[..6].ToUpperInvariant();
        var request = Good("Http " + code, code.ToLowerInvariant());
        using var admin = await api.ClientAsync(ApiHarness.Admin);
        using var staff = await api.ClientAsync(ApiHarness.StoreStaff);
        Guid? id = null;
        try
        {
            Assert.Equal(HttpStatusCode.Forbidden, (await staff.PostAsJsonAsync("/api/admin/stores", request)).StatusCode);
            using var created = await admin.PostAsJsonAsync("/api/admin/stores", request);
            Assert.True(created.StatusCode == HttpStatusCode.OK, await created.Content.ReadAsStringAsync());
            var data = JsonDocument.Parse(await created.Content.ReadAsStringAsync()).RootElement.GetProperty("data");
            id = data.GetProperty("id").GetGuid();
            Assert.Equal(code, data.GetProperty("code").GetString());

            Assert.Equal(HttpStatusCode.Conflict, (await admin.PostAsJsonAsync("/api/admin/stores", request with { Name = "Other " + code })).StatusCode); // same code
            Assert.Equal(HttpStatusCode.BadRequest, (await admin.PutAsJsonAsync($"/api/admin/stores/{id}", request with { ServiceRadiusKm = 0 })).StatusCode);
            Assert.Equal(HttpStatusCode.NotFound, (await admin.PutAsJsonAsync($"/api/admin/stores/{Guid.NewGuid()}", request)).StatusCode);
            Assert.Equal(HttpStatusCode.OK, (await admin.PutAsJsonAsync($"/api/admin/stores/{id}", request with { ServiceRadiusKm = 3, IsActive = false })).StatusCode);
            // Closed stores leave the shared list but stay in the administrator's one.
            Assert.DoesNotContain(code, await admin.GetStringAsync("/api/stores"));
            Assert.Contains(code, await admin.GetStringAsync("/api/admin/stores"));
        }
        finally
        {
            if (id is { } made)
            {
                await using var scope = api.Services.CreateAsyncScope();
                await scope.ServiceProvider.GetRequiredService<QuickCommerceDbContext>().Stores.Where(store => store.Id == made).ExecuteDeleteAsync();
            }
        }
    }
}
