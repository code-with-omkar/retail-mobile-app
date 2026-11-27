using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using QuickCommerce.Application.Services;
using QuickCommerce.Infrastructure.Persistence;
using Xunit;

namespace QuickCommerce.Tests;

/// <summary>A new member of staff over HTTP (opt-in, disposable database): temporary password, forced change, then normal use.</summary>
[Collection("SqlServer")]
public sealed class StaffPasswordHttpTests : IClassFixture<ApiHarness>
{
    private readonly ApiHarness api;

    public StaffPasswordHttpTests(ApiHarness api) => this.api = api;

    private static JsonElement Data(string json) => JsonDocument.Parse(json).RootElement.GetProperty("data");

    [Fact]
    public void A_temporary_password_is_long_and_mixed()
    {
        var made = Enumerable.Range(0, 50).Select(_ => TemporaryPasswords.Create()).ToArray();

        Assert.All(made, password =>
        {
            Assert.Equal(TemporaryPasswords.Length, password.Length);
            Assert.Contains(password, char.IsUpper);
            Assert.Contains(password, char.IsLower);
            Assert.Contains(password, char.IsDigit);
        });
        Assert.Equal(made.Length, made.Distinct().Count());
    }

    [Fact]
    public async Task A_new_member_of_staff_signs_in_with_a_temporary_password_and_must_change_it_first()
    {
        if (ApiHarness.ConnectionString is null) { return; }
        using var admin = await api.ClientAsync(ApiHarness.Admin);
        var email = $"staff-{Guid.NewGuid():N}@example.test";
        Guid organizationId;
        await using (var scope = api.Services.CreateAsyncScope())
        {
            organizationId = await scope.ServiceProvider.GetRequiredService<QuickCommerceDbContext>().Users.Where(user => user.Id == ApiHarness.Admin).Select(user => user.OrganizationId).SingleAsync();
        }

        using var created = await admin.PostAsJsonAsync("/api/users", new { firstName = "New", lastName = "Manager", email, organizationId, role = "StoreStaff", staffCategory = "StoreManager", isActive = true });
        Assert.True(created.IsSuccessStatusCode, await created.Content.ReadAsStringAsync());
        var userId = Data(await created.Content.ReadAsStringAsync()).GetProperty("id").GetGuid();
        try
        {
            using var made = await admin.PostAsync($"/api/users/{userId}/temporary-password", null);
            Assert.Equal(HttpStatusCode.OK, made.StatusCode);
            Assert.Equal("no-store", made.Headers.CacheControl!.ToString());
            var temporary = Data(await made.Content.ReadAsStringAsync()).GetProperty("temporaryPassword").GetString()!;

            // Not for yourself, not for a customer, and not by anyone but an administrator.
            Assert.Equal(HttpStatusCode.Conflict, (await admin.PostAsync($"/api/users/{ApiHarness.Admin}/temporary-password", null)).StatusCode);
            Assert.Equal(HttpStatusCode.Conflict, (await admin.PostAsync($"/api/users/{ApiHarness.Customer}/temporary-password", null)).StatusCode);
            using var staff = await api.ClientAsync(ApiHarness.StoreStaff);
            Assert.Equal(HttpStatusCode.Forbidden, (await staff.PostAsync($"/api/users/{userId}/temporary-password", null)).StatusCode);

            using var person = await api.ClientAsync(null);
            using var signedIn = await person.PostAsJsonAsync("/api/auth/login", new { username = email, password = temporary });
            Assert.Equal(HttpStatusCode.OK, signedIn.StatusCode);
            var session = Data(await signedIn.Content.ReadAsStringAsync());
            Assert.True(session.GetProperty("user").GetProperty("mustChangePassword").GetBoolean());
            person.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", session.GetProperty("accessToken").GetString());

            using var blocked = await person.GetAsync("/api/orders/paged");
            Assert.Equal(HttpStatusCode.Forbidden, blocked.StatusCode);
            Assert.Contains("PasswordChangeRequired", await blocked.Content.ReadAsStringAsync());

            var refreshToken = session.GetProperty("refreshToken").GetString();
            using var same = await person.PostAsJsonAsync("/api/auth/change-password", new { currentPassword = temporary, newPassword = temporary, refreshToken });
            Assert.Equal(HttpStatusCode.BadRequest, same.StatusCode);
            var chosen = "My-own-passw0rd-2026";
            using var changed = await person.PostAsJsonAsync("/api/auth/change-password", new { currentPassword = temporary, newPassword = chosen, refreshToken });
            Assert.True(changed.StatusCode == HttpStatusCode.OK, await changed.Content.ReadAsStringAsync());

            // A fresh token from the same session no longer carries the mark.
            using var refreshed = await person.PostAsJsonAsync("/api/auth/refresh", new { refreshToken });
            Assert.Equal(HttpStatusCode.OK, refreshed.StatusCode);
            var next = Data(await refreshed.Content.ReadAsStringAsync());
            Assert.False(next.GetProperty("user").GetProperty("mustChangePassword").GetBoolean());
            person.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", next.GetProperty("accessToken").GetString());
            Assert.Equal(HttpStatusCode.OK, (await person.GetAsync("/api/orders/paged")).StatusCode);

            // The reset by email now works for staff too (the answer is the same either way, so look for the code it made).
            using var anonymous = await api.ClientAsync(null);
            Assert.Equal(HttpStatusCode.OK, (await anonymous.PostAsJsonAsync("/api/auth/forgot-password", new { email })).StatusCode);
            await using var check = api.Services.CreateAsyncScope();
            Assert.True(await check.ServiceProvider.GetRequiredService<QuickCommerceDbContext>().PasswordResetCodes.AnyAsync(code => code.UserId == userId));
        }
        finally
        {
            await using var scope = api.Services.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<QuickCommerceDbContext>();
            await db.PasswordResetCodes.Where(item => item.UserId == userId).ExecuteDeleteAsync();
            await db.RefreshTokens.Where(item => item.UserId == userId).ExecuteDeleteAsync();
            await db.UserCredentials.Where(item => item.UserId == userId).ExecuteDeleteAsync();
            await db.Users.Where(item => item.Id == userId).ExecuteDeleteAsync();
        }
    }
}
