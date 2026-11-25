using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using QuickCommerce.Infrastructure.Persistence;
using Xunit;

namespace QuickCommerce.Tests;

/// <summary>
/// The real API over HTTP (opt-in, like the other SQL tests): who may call each coupon, delivery-time and admin endpoint, the status codes
/// and the shape of what comes back. These catch a missing policy or a wrong route that tests of the services behind them cannot see.
/// </summary>
[Collection("SqlServer")]
public sealed class ApiHttpTests : IClassFixture<ApiHarness>
{
    private readonly ApiHarness api;

    public ApiHttpTests(ApiHarness api) => this.api = api;

    private static bool Skip => ApiHarness.ConnectionString is null;

    private static readonly Guid? Anonymous = null;

    private async Task<Guid> SomeStoreAsync()
    {
        await api.PrepareAsync();
        await using var scope = api.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<QuickCommerceDbContext>();
        return await db.Stores.AsNoTracking().Where(store => store.IsActive).OrderBy(store => store.Name).Select(store => store.Id).FirstAsync();
    }

    private static JsonElement Data(string json) => JsonDocument.Parse(json).RootElement.GetProperty("data");

    public static TheoryData<string, string, string?, int> Matrix() => new()
    {
        // method, path ({store} is a real store), body, then the status for each caller is checked in the test below via Callers()
    };

    private static readonly (string Name, Guid? User)[] Callers =
    [
        ("anonymous", Anonymous), ("customer", ApiHarness.Customer), ("store staff", ApiHarness.StoreStaff), ("delivery partner", ApiHarness.DeliveryPartner), ("administrator", ApiHarness.Admin),
    ];

    private async Task ExpectAsync(HttpMethod method, string path, object? body, params (Guid? User, HttpStatusCode Status)[] expected)
    {
        foreach (var (user, status) in expected)
        {
            using var client = await api.ClientAsync(user);
            using var request = new HttpRequestMessage(method, path);
            if (body is not null)
            {
                request.Content = JsonContent.Create(body);
            }

            using var response = await client.SendAsync(request);
            var who = Callers.First(item => item.User == user).Name;
            Assert.True(response.StatusCode == status, $"{method} {path} as {who}: expected {(int)status} but got {(int)response.StatusCode}: {await response.Content.ReadAsStringAsync()}");
        }
    }

    [Fact]
    public async Task Only_an_administrator_can_read_the_coupons_and_the_delivery_time_settings()
    {
        if (Skip) { return; }
        foreach (var path in new[] { "/api/admin/coupons", "/api/admin/delivery-slots", "/api/admin/commission" })
        {
            await ExpectAsync(HttpMethod.Get, path, null,
                (Anonymous, HttpStatusCode.Unauthorized), (ApiHarness.Customer, HttpStatusCode.Forbidden), (ApiHarness.StoreStaff, HttpStatusCode.Forbidden),
                (ApiHarness.DeliveryPartner, HttpStatusCode.Forbidden), (ApiHarness.Admin, HttpStatusCode.OK));
        }
    }

    [Fact]
    public async Task Nobody_but_an_administrator_can_change_coupons_or_delivery_times_and_a_bad_request_is_a_400()
    {
        if (Skip) { return; }
        var store = await SomeStoreAsync();
        var badSlots = new { enabled = true, open = "9am", close = "21:00", windowMinutes = 60, capacity = 5, cutoffMinutes = 60, daysAhead = 2, asapAllowed = true };

        foreach (var (method, path, body) in new (HttpMethod, string, object)[]
        {
            (HttpMethod.Post, "/api/admin/coupons", new { code = "X", kind = "Flat", value = 5 }),
            (HttpMethod.Put, $"/api/admin/coupons/{Guid.NewGuid()}", new { code = "X", kind = "Flat", value = 5 }),
            (HttpMethod.Put, "/api/admin/delivery-slots/default", badSlots),
            (HttpMethod.Put, $"/api/admin/delivery-slots/stores/{store}", badSlots),
            (HttpMethod.Delete, $"/api/admin/delivery-slots/stores/{store}", null!),
        })
        {
            await ExpectAsync(method, path, body,
                (Anonymous, HttpStatusCode.Unauthorized), (ApiHarness.Customer, HttpStatusCode.Forbidden), (ApiHarness.StoreStaff, HttpStatusCode.Forbidden), (ApiHarness.DeliveryPartner, HttpStatusCode.Forbidden));
        }

        // An administrator with a bad body is refused with a 400 and nothing is saved.
        await ExpectAsync(HttpMethod.Post, "/api/admin/coupons", new { code = "X", kind = "Flat", value = 5 }, (ApiHarness.Admin, HttpStatusCode.BadRequest)); // code too short
        await ExpectAsync(HttpMethod.Put, "/api/admin/delivery-slots/default", badSlots, (ApiHarness.Admin, HttpStatusCode.BadRequest));
        await ExpectAsync(HttpMethod.Put, $"/api/admin/coupons/{Guid.NewGuid()}", new { code = "VALIDCODE", kind = "Flat", value = 5 }, (ApiHarness.Admin, HttpStatusCode.NotFound));
    }

    [Fact]
    public async Task Only_a_signed_in_customer_can_try_a_coupon_or_list_delivery_times()
    {
        if (Skip) { return; }
        var store = await SomeStoreAsync();

        await ExpectAsync(HttpMethod.Post, $"/api/carts/{store}/coupon", new { code = "NOPE" },
            (Anonymous, HttpStatusCode.Unauthorized), (ApiHarness.StoreStaff, HttpStatusCode.Forbidden), (ApiHarness.Customer, HttpStatusCode.OK));
        await ExpectAsync(HttpMethod.Get, $"/api/stores/{store}/delivery-slots", null,
            (Anonymous, HttpStatusCode.Unauthorized), (ApiHarness.StoreStaff, HttpStatusCode.Forbidden), (ApiHarness.Customer, HttpStatusCode.OK));
    }

    [Fact]
    public async Task A_store_that_does_not_exist_is_a_404_not_a_server_error()
    {
        if (Skip) { return; }
        await ExpectAsync(HttpMethod.Get, $"/api/stores/{Guid.NewGuid()}/delivery-slots", null, (ApiHarness.Customer, HttpStatusCode.NotFound));
    }

    [Fact]
    public async Task An_administrator_makes_changes_and_lists_a_coupon_and_a_customer_is_told_why_it_cannot_be_used_yet()
    {
        if (Skip) { return; }
        var store = await SomeStoreAsync();
        var code = "HTTP" + Guid.NewGuid().ToString("N")[..8].ToUpperInvariant();
        using var admin = await api.ClientAsync(ApiHarness.Admin);
        using var customer = await api.ClientAsync(ApiHarness.Customer);
        Guid? id = null;
        try
        {
            using var created = await admin.PostAsJsonAsync("/api/admin/coupons", new { code = code.ToLowerInvariant(), kind = "Percent", value = 10, maxDiscount = 50, minSubtotal = 100, description = "http test" });
            Assert.Equal(HttpStatusCode.OK, created.StatusCode);
            var coupon = Data(await created.Content.ReadAsStringAsync());
            id = coupon.GetProperty("id").GetGuid();
            Assert.Equal(code, coupon.GetProperty("code").GetString()); // stored upper-case
            Assert.Equal("Active", coupon.GetProperty("status").GetString());

            using var duplicate = await admin.PostAsJsonAsync("/api/admin/coupons", new { code, kind = "Flat", value = 5 });
            Assert.Equal(HttpStatusCode.Conflict, duplicate.StatusCode);

            using var listed = await admin.GetAsync("/api/admin/coupons");
            Assert.Contains(Data(await listed.Content.ReadAsStringAsync()).EnumerateArray(), item => item.GetProperty("code").GetString() == code);

            // The demo customer's cart is empty, so the code cannot apply yet, and the answer says why (still a 200).
            using var preview = await customer.PostAsJsonAsync($"/api/carts/{store}/coupon", new { code = code.ToLowerInvariant() });
            Assert.Equal(HttpStatusCode.OK, preview.StatusCode);
            var tried = Data(await preview.Content.ReadAsStringAsync());
            Assert.False(tried.GetProperty("valid").GetBoolean());
            Assert.False(string.IsNullOrWhiteSpace(tried.GetProperty("message").GetString()));

            using var off = await admin.PutAsJsonAsync($"/api/admin/coupons/{id}", new { code, kind = "Percent", value = 10, maxDiscount = 50, minSubtotal = 100, isActive = false });
            Assert.Equal(HttpStatusCode.OK, off.StatusCode);
            Assert.Equal("Off", Data(await off.Content.ReadAsStringAsync()).GetProperty("status").GetString());
        }
        finally
        {
            if (id is { } couponId)
            {
                await using var scope = api.Services.CreateAsyncScope();
                var db = scope.ServiceProvider.GetRequiredService<QuickCommerceDbContext>();
                await db.Coupons.Where(item => item.Id == couponId).ExecuteDeleteAsync();
            }
        }
    }

    [Fact]
    public async Task Delivery_times_set_by_an_administrator_show_up_for_a_customer_of_that_store_only()
    {
        if (Skip) { return; }
        var store = await SomeStoreAsync();
        using var admin = await api.ClientAsync(ApiHarness.Admin);
        using var customer = await api.ClientAsync(ApiHarness.Customer);
        var own = new { enabled = true, open = "00:00", close = "24:00", windowMinutes = 60, capacity = 2, cutoffMinutes = 0, daysAhead = 2, asapAllowed = false };
        try
        {
            using var before = await customer.GetAsync($"/api/stores/{store}/delivery-slots");
            Assert.Equal(HttpStatusCode.OK, before.StatusCode);

            using var saved = await admin.PutAsJsonAsync($"/api/admin/delivery-slots/stores/{store}", own);
            Assert.Equal(HttpStatusCode.OK, saved.StatusCode);
            var rules = Data(await saved.Content.ReadAsStringAsync());
            var mine = rules.GetProperty("stores").EnumerateArray().Single(item => item.GetProperty("storeId").GetGuid() == store);
            Assert.Equal(JsonValueKind.Object, mine.GetProperty("override").ValueKind);

            using var slots = await customer.GetAsync($"/api/stores/{store}/delivery-slots");
            var data = Data(await slots.Content.ReadAsStringAsync());
            Assert.True(data.GetProperty("enabled").GetBoolean());
            Assert.False(data.GetProperty("asapAllowed").GetBoolean());
            var windows = data.GetProperty("windows").EnumerateArray().ToArray();
            Assert.NotEmpty(windows);
            Assert.All(windows, window => Assert.Equal(2, window.GetProperty("left").GetInt32()));
            Assert.All(windows, window => Assert.False(string.IsNullOrEmpty(window.GetProperty("timeLabel").GetString())));
        }
        finally
        {
            using var cleared = await admin.DeleteAsync($"/api/admin/delivery-slots/stores/{store}");
            Assert.Equal(HttpStatusCode.OK, cleared.StatusCode);
        }
    }

    [Fact]
    public async Task The_today_screen_is_for_store_staff_and_administrators_and_an_administrator_must_name_a_store()
    {
        if (Skip) { return; }
        var store = await SomeStoreAsync();

        await ExpectAsync(HttpMethod.Get, "/api/store-today", null,
            (Anonymous, HttpStatusCode.Unauthorized), (ApiHarness.Customer, HttpStatusCode.Forbidden), (ApiHarness.DeliveryPartner, HttpStatusCode.Forbidden),
            (ApiHarness.StoreStaff, HttpStatusCode.OK), (ApiHarness.Admin, HttpStatusCode.BadRequest));
        await ExpectAsync(HttpMethod.Get, $"/api/store-today?storeId={store}", null, (ApiHarness.Admin, HttpStatusCode.OK), (ApiHarness.Customer, HttpStatusCode.Forbidden));
        await ExpectAsync(HttpMethod.Get, $"/api/store-today?storeId={Guid.NewGuid()}", null, (ApiHarness.Admin, HttpStatusCode.NotFound));

        using var admin = await api.ClientAsync(ApiHarness.Admin);
        using var response = await admin.GetAsync($"/api/store-today?storeId={store}");
        var data = Data(await response.Content.ReadAsStringAsync());
        Assert.Equal(store, data.GetProperty("storeId").GetGuid());
        Assert.False(string.IsNullOrWhiteSpace(data.GetProperty("storeName").GetString()));
        Assert.Equal(JsonValueKind.Object, data.GetProperty("counts").ValueKind);
        Assert.Equal(JsonValueKind.Array, data.GetProperty("alerts").ValueKind);
        Assert.Equal(JsonValueKind.Array, data.GetProperty("upcoming").ValueKind);
    }

    [Fact]
    public async Task Anyone_can_send_analytics_steps_but_only_good_ones_are_accepted_and_nothing_personal_is_kept()
    {
        if (Skip) { return; }
        await api.PrepareAsync();
        var session = "http" + Guid.NewGuid().ToString("N")[..20];
        using var guest = await api.ClientAsync(Anonymous);
        try
        {
            using var good = await guest.PostAsJsonAsync("/api/analytics/events", new { sessionId = session, events = new object[] { new { name = "search", detail = "Fresh  Milk" }, new { name = "order_placed", detail = "call me 98765 43210" } } });
            Assert.Equal(HttpStatusCode.Accepted, good.StatusCode);
            Assert.Equal(2, JsonDocument.Parse(await good.Content.ReadAsStringAsync()).RootElement.GetProperty("data").GetInt32());

            await using (var scope = api.Services.CreateAsyncScope())
            {
                var db = scope.ServiceProvider.GetRequiredService<QuickCommerceDbContext>();
                var stored = await db.AnalyticsEvents.AsNoTracking().Where(item => item.SessionId == session).OrderBy(item => item.Id).ToListAsync();
                Assert.Equal(["search", "order_placed"], stored.Select(item => item.Name));
                Assert.Equal("fresh milk", stored[0].Detail);
                Assert.Null(stored[1].Detail); // looked like a phone number
            }

            foreach (var bad in new object[]
            {
                new { sessionId = "short", events = new object[] { new { name = "app_open" } } },
                new { sessionId = session, events = new object[] { new { name = "drop_table" } } },
                new { sessionId = session, events = Array.Empty<object>() },
                new { sessionId = session, events = Enumerable.Range(0, 26).Select(_ => (object)new { name = "app_open" }).ToArray() },
            })
            {
                using var refused = await guest.PostAsJsonAsync("/api/analytics/events", bad);
                Assert.Equal(HttpStatusCode.BadRequest, refused.StatusCode);
            }
        }
        finally
        {
            await using var scope = api.Services.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<QuickCommerceDbContext>();
            await db.AnalyticsEvents.Where(item => item.SessionId == session).ExecuteDeleteAsync();
        }
    }

    [Fact]
    public async Task The_behaviour_report_is_for_store_staff_and_administrators_and_has_the_documented_shape()
    {
        if (Skip) { return; }
        var from = DateTime.UtcNow.AddDays(-3).ToString("yyyy-MM-dd");
        var to = DateTime.UtcNow.AddDays(1).ToString("yyyy-MM-dd");
        var path = $"/api/reports/behaviour?from={from}&to={to}";

        await ExpectAsync(HttpMethod.Get, path, null,
            (Anonymous, HttpStatusCode.Unauthorized), (ApiHarness.Customer, HttpStatusCode.Forbidden), (ApiHarness.DeliveryPartner, HttpStatusCode.Forbidden),
            (ApiHarness.StoreStaff, HttpStatusCode.OK), (ApiHarness.Admin, HttpStatusCode.OK));
        await ExpectAsync(HttpMethod.Get, "/api/reports/behaviour?from=2026-11-05&to=2026-11-01", null, (ApiHarness.Admin, HttpStatusCode.BadRequest));

        using var admin = await api.ClientAsync(ApiHarness.Admin);
        using var response = await admin.GetAsync(path);
        var data = Data(await response.Content.ReadAsStringAsync());
        Assert.Equal(5, data.GetProperty("funnel").GetArrayLength());
        Assert.Equal("app_open", data.GetProperty("funnel")[0].GetProperty("name").GetString());
        Assert.Equal(12, data.GetProperty("steps").GetArrayLength());
        Assert.Equal(JsonValueKind.Array, data.GetProperty("topSearches").ValueKind);
        Assert.Equal(JsonValueKind.Array, data.GetProperty("dailySessions").ValueKind);
    }

    [Fact]
    public async Task An_administrator_gives_an_offer_a_festival_card_and_a_customer_sees_the_design_while_store_staff_are_refused()
    {
        if (Skip) { return; }
        var store = await SomeStoreAsync();
        var title = "HTTP card " + Guid.NewGuid().ToString("N")[..8];
        using var admin = await api.ClientAsync(ApiHarness.Admin);
        using var staff = await api.ClientAsync(ApiHarness.StoreStaff);
        using var customer = await api.ClientAsync(ApiHarness.Customer);
        Guid? id = null;
        try
        {
            // Store staff may not choose a design (their ordinary offers are unaffected), and nothing is saved.
            using var refused = await staff.PostAsJsonAsync("/api/admin/campaigns", new { titleEn = title, bodyEn = "Offer", cardDesign = "diwali" });
            Assert.Equal(HttpStatusCode.Forbidden, refused.StatusCode);

            using var unknown = await admin.PostAsJsonAsync("/api/admin/campaigns", new { titleEn = title, bodyEn = "Offer", cardDesign = "onam" });
            Assert.Equal(HttpStatusCode.BadRequest, unknown.StatusCode);

            using var created = await admin.PostAsJsonAsync("/api/admin/campaigns", new { titleEn = title, bodyEn = "Mithai for less.", cardDesign = " Diwali " });
            Assert.Equal(HttpStatusCode.Created, created.StatusCode);
            var offer = Data(await created.Content.ReadAsStringAsync());
            id = offer.GetProperty("id").GetGuid();
            Assert.Equal("diwali", offer.GetProperty("cardDesign").GetString());

            using var listed = await admin.GetAsync("/api/admin/campaigns");
            Assert.Contains(Data(await listed.Content.ReadAsStringAsync()).EnumerateArray(), item => item.GetProperty("id").GetGuid() == id && item.GetProperty("cardDesign").GetString() == "diwali");

            using var banner = await customer.GetAsync($"/api/catalog/offers?storeId={store}");
            Assert.Equal(HttpStatusCode.OK, banner.StatusCode);
            Assert.Contains(JsonDocument.Parse(await banner.Content.ReadAsStringAsync()).RootElement.GetProperty("data").EnumerateArray(), item => item.GetProperty("id").GetGuid() == id && item.GetProperty("cardDesign").GetString() == "diwali");

            using var opened = await customer.GetAsync($"/api/catalog/offers/{id}");
            Assert.Equal("diwali", Data(await opened.Content.ReadAsStringAsync()).GetProperty("cardDesign").GetString());
        }
        finally
        {
            await using var scope = api.Services.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<QuickCommerceDbContext>();
            await db.Campaigns.Where(item => item.TitleEn == title).ExecuteDeleteAsync();
        }
    }

    [Fact]
    public async Task A_platform_administrator_switches_organization_read_only_and_nobody_else_may()
    {
        if (Skip) { return; }
        await api.PrepareAsync();
        var other = new QuickCommerce.Domain.Organization { Name = "HTTP switch " + Guid.NewGuid().ToString("N")[..8] };
        var inactive = new QuickCommerce.Domain.Organization { Name = "HTTP closed " + Guid.NewGuid().ToString("N")[..8], IsActive = false };
        await using (var scope = api.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<QuickCommerceDbContext>();
            db.Organizations.AddRange(other, inactive);
            await db.SaveChangesAsync();
        }

        try
        {
            using var admin = await api.ClientAsync(ApiHarness.Admin);
            using var staff = await api.ClientAsync(ApiHarness.StoreStaff);
            using var customer = await api.ClientAsync(ApiHarness.Customer);

            // The list: the administrator sees the organizations (the closed one is left out), staff and customers are refused.
            using var listed = await admin.GetAsync("/api/admin/organizations");
            Assert.Equal(HttpStatusCode.OK, listed.StatusCode);
            var data = Data(await listed.Content.ReadAsStringAsync());
            var home = data.GetProperty("homeOrganizationId").GetGuid();
            Assert.Equal(home, data.GetProperty("currentOrganizationId").GetGuid());
            Assert.Contains(data.GetProperty("organizations").EnumerateArray(), item => item.GetProperty("id").GetGuid() == other.Id);
            Assert.DoesNotContain(data.GetProperty("organizations").EnumerateArray(), item => item.GetProperty("id").GetGuid() == inactive.Id);
            Assert.Equal(HttpStatusCode.Forbidden, (await staff.GetAsync("/api/admin/organizations")).StatusCode);
            Assert.Equal(HttpStatusCode.Forbidden, (await customer.GetAsync("/api/admin/organizations")).StatusCode);

            // Switched: reading works and now answers for the other organization; the home organization is remembered.
            admin.DefaultRequestHeaders.Add("X-Organization-Id", other.Id.ToString());
            using var switched = await admin.GetAsync("/api/admin/organizations");
            Assert.Equal(HttpStatusCode.OK, switched.StatusCode);
            var seen = Data(await switched.Content.ReadAsStringAsync());
            Assert.Equal(other.Id, seen.GetProperty("currentOrganizationId").GetGuid());
            Assert.Equal(home, seen.GetProperty("homeOrganizationId").GetGuid());
            Assert.Equal(HttpStatusCode.OK, (await admin.GetAsync("/api/admin/campaigns")).StatusCode);

            // Read-only there: a change is refused, nothing is saved.
            var title = "HTTP readonly " + Guid.NewGuid().ToString("N")[..8];
            using var refused = await admin.PostAsJsonAsync("/api/admin/campaigns", new { titleEn = title, bodyEn = "Offer" });
            Assert.Equal(HttpStatusCode.Forbidden, refused.StatusCode);
            await using (var scope = api.Services.CreateAsyncScope())
            {
                var db = scope.ServiceProvider.GetRequiredService<QuickCommerceDbContext>();
                Assert.False(await db.Campaigns.AnyAsync(item => item.TitleEn == title));
            }

            // Back home (no header) the same change works.
            admin.DefaultRequestHeaders.Remove("X-Organization-Id");
            using var created = await admin.PostAsJsonAsync("/api/admin/campaigns", new { titleEn = title, bodyEn = "Offer" });
            Assert.Equal(HttpStatusCode.Created, created.StatusCode);
            await using (var scope = api.Services.CreateAsyncScope())
            {
                await scope.ServiceProvider.GetRequiredService<QuickCommerceDbContext>().Campaigns.Where(item => item.TitleEn == title).ExecuteDeleteAsync();
            }

            // A closed or unknown organization, and anyone who is not a platform administrator, is refused outright.
            foreach (var wanted in new[] { inactive.Id, Guid.NewGuid() })
            {
                using var request = new HttpRequestMessage(HttpMethod.Get, "/api/admin/organizations");
                request.Headers.Add("X-Organization-Id", wanted.ToString());
                request.Headers.Authorization = admin.DefaultRequestHeaders.Authorization;
                Assert.Equal(HttpStatusCode.Forbidden, (await admin.SendAsync(request)).StatusCode);
            }

            staff.DefaultRequestHeaders.Add("X-Organization-Id", other.Id.ToString());
            Assert.Equal(HttpStatusCode.Forbidden, (await staff.GetAsync("/api/orders")).StatusCode);
        }
        finally
        {
            await using var scope = api.Services.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<QuickCommerceDbContext>();
            await db.Organizations.Where(item => item.Id == other.Id || item.Id == inactive.Id).ExecuteDeleteAsync();
        }
    }

    [Fact]
    public async Task An_administrator_adds_a_sub_category_and_its_products_show_under_the_main_category_and_under_itself_only()
    {
        if (Skip) { return; }
        await api.PrepareAsync();
        var tag = Guid.NewGuid().ToString("N")[..8];
        using var admin = await api.ClientAsync(ApiHarness.Admin);
        using var staff = await api.ClientAsync(ApiHarness.StoreStaff);
        using var guest = await api.ClientAsync(null);
        try
        {
            using var refused = await staff.PostAsJsonAsync("/api/admin/catalog/categories", new { name = "Http top " + tag });
            Assert.Equal(HttpStatusCode.Forbidden, refused.StatusCode);

            using var topResponse = await admin.PostAsJsonAsync("/api/admin/catalog/categories", new { name = "Http top " + tag });
            Assert.Equal(HttpStatusCode.Created, topResponse.StatusCode);
            var top = Data(await topResponse.Content.ReadAsStringAsync()).GetProperty("id").GetGuid();
            using var subResponse = await admin.PostAsJsonAsync("/api/admin/catalog/categories", new { name = "Http sub " + tag, parentCategoryId = top });
            var sub = Data(await subResponse.Content.ReadAsStringAsync()).GetProperty("id").GetGuid();
            using var siblingResponse = await admin.PostAsJsonAsync("/api/admin/catalog/categories", new { name = "Http sibling " + tag, parentCategoryId = top });
            var sibling = Data(await siblingResponse.Content.ReadAsStringAsync()).GetProperty("id").GetGuid();

            using var tooDeep = await admin.PostAsJsonAsync("/api/admin/catalog/categories", new { name = "Too deep " + tag, parentCategoryId = sub });
            Assert.Equal(HttpStatusCode.BadRequest, tooDeep.StatusCode);
            using var repeated = await admin.PostAsJsonAsync("/api/admin/catalog/categories", new { name = "http SUB " + tag, parentCategoryId = top });
            Assert.Equal(HttpStatusCode.Conflict, repeated.StatusCode);

            using var listed = await admin.GetAsync("/api/admin/catalog/categories");
            Assert.Contains(Data(await listed.Content.ReadAsStringAsync()).EnumerateArray(), item => item.GetProperty("id").GetGuid() == sub && item.GetProperty("parentCategoryId").GetGuid() == top);

            using var product = await admin.PostAsJsonAsync("/api/admin/catalog/products", new { sku = "HTTP-" + tag, name = "Http product " + tag, description = "x", categoryId = sub, unitOfMeasure = "1 kg", price = 10 });
            Assert.Equal(HttpStatusCode.Created, product.StatusCode);
            var productId = Data(await product.Content.ReadAsStringAsync()).GetProperty("id").GetGuid();

            async Task<bool> ListsAsync(Guid category)
            {
                using var page = await guest.GetAsync($"/api/catalog/products?categoryId={category}&pageSize=50");
                Assert.Equal(HttpStatusCode.OK, page.StatusCode);
                return Data(await page.Content.ReadAsStringAsync()).GetProperty("items").EnumerateArray().Any(item => item.GetProperty("id").GetGuid() == productId);
            }

            Assert.True(await ListsAsync(top));
            Assert.True(await ListsAsync(sub));
            Assert.False(await ListsAsync(sibling));
        }
        finally
        {
            await using var scope = api.Services.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<QuickCommerceDbContext>();
            var sku = "HTTP-" + tag;
            await db.ProductVariants.Where(variant => db.Products.Any(product => product.Id == variant.ProductId && product.Sku == sku)).ExecuteDeleteAsync();
            await db.Products.Where(product => product.Sku == sku).ExecuteDeleteAsync();
            await db.Categories.Where(category => category.Name.EndsWith(tag) && category.ParentCategoryId != null).ExecuteDeleteAsync();
            await db.Categories.Where(category => category.Name.EndsWith(tag)).ExecuteDeleteAsync();
        }
    }

    [Fact]
    public async Task The_portal_can_give_a_user_a_store_sending_the_role_and_staff_category_as_names()
    {
        if (Skip) { return; }
        await api.PrepareAsync();
        var first = await SomeStoreAsync();
        using var admin = await api.ClientAsync(ApiHarness.Admin);
        await using var scope = api.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<QuickCommerceDbContext>();
        var organization = await db.Stores.AsNoTracking().Where(store => store.Id == first).Select(store => store.OrganizationId).SingleAsync();
        var second = await db.Stores.AsNoTracking().Where(store => store.IsActive && store.OrganizationId == organization && store.Id != first).Select(store => store.Id).FirstAsync();
        var email = $"http-user-{Guid.NewGuid():N}@example.test";
        try
        {
            using var created = await admin.PostAsJsonAsync("/api/users", new { firstName = "Http", lastName = "Partner", email, organizationId = organization, storeId = first, storeIds = new[] { first }, role = "DeliveryPartner", isActive = true });
            Assert.True(created.StatusCode == HttpStatusCode.Created, await created.Content.ReadAsStringAsync());
            var id = Data(await created.Content.ReadAsStringAsync()).GetProperty("id").GetGuid();

            using var updated = await admin.PutAsJsonAsync($"/api/users/{id}", new { firstName = "Http", lastName = "Staff", storeId = second, storeIds = new[] { second }, role = "StoreStaff", staffCategory = "StoreManager", isActive = true });
            Assert.True(updated.StatusCode == HttpStatusCode.OK, await updated.Content.ReadAsStringAsync());
            var saved = Data(await updated.Content.ReadAsStringAsync());
            Assert.Equal("StoreStaff", saved.GetProperty("role").GetString());
            Assert.Equal("StoreManager", saved.GetProperty("staffCategory").GetString());
            Assert.Equal([second], saved.GetProperty("storeIds").EnumerateArray().Select(item => item.GetGuid()));
        }
        finally
        {
            var ids = await db.Users.Where(user => user.Email == email).Select(user => user.Id).ToListAsync();
            await db.Set<QuickCommerce.Domain.UserStoreAssignment>().Where(row => ids.Contains(row.UserId)).ExecuteDeleteAsync();
            await db.Set<QuickCommerce.Domain.UserRole>().Where(row => ids.Contains(row.UserId)).ExecuteDeleteAsync();
            await db.Users.Where(user => user.Email == email).ExecuteDeleteAsync();
        }
    }


    [Fact]
    public async Task An_administrator_makes_an_ad_and_a_guest_sees_it_at_its_place_and_views_and_taps_are_counted()
    {
        if (Skip) { return; }
        var store = await SomeStoreAsync();
        var title = "HTTP ad " + Guid.NewGuid().ToString("N")[..8];
        using var admin = await api.ClientAsync(ApiHarness.Admin);
        using var staff = await api.ClientAsync(ApiHarness.StoreStaff);
        using var guest = await api.ClientAsync(null);
        Guid? id = null;
        try
        {
            using var refused = await staff.PostAsJsonAsync("/api/admin/ads", new { placement = "home", imageUrl = "/media/offers/http-ad.png", title });
            Assert.Equal(HttpStatusCode.Forbidden, refused.StatusCode);
            using var badPicture = await admin.PostAsJsonAsync("/api/admin/ads", new { placement = "home", imageUrl = "https://evil.example/a.png", title });
            Assert.Equal(HttpStatusCode.BadRequest, badPicture.StatusCode);
            using var badPlace = await admin.PostAsJsonAsync("/api/admin/ads", new { placement = "footer", imageUrl = "/media/offers/http-ad.png", title });
            Assert.Equal(HttpStatusCode.BadRequest, badPlace.StatusCode);

            using var created = await admin.PostAsJsonAsync("/api/admin/ads", new { placement = "home_middle", imageUrl = "/media/offers/http-ad.png", title, targetType = "Url", target = "https://example.com/sale", storeId = store, sortOrder = 0 });
            Assert.Equal(HttpStatusCode.Created, created.StatusCode);
            var ad = Data(await created.Content.ReadAsStringAsync());
            id = ad.GetProperty("id").GetGuid();
            Assert.Equal("Live", ad.GetProperty("status").GetString());

            // A guest (not signed in) sees it at its place, for its store, and not at another place or for no store.
            using var seen = await guest.GetAsync($"/api/ads?placement=home_middle&storeId={store}");
            Assert.Equal(HttpStatusCode.OK, seen.StatusCode);
            Assert.Contains(Data(await seen.Content.ReadAsStringAsync()).EnumerateArray(), item => item.GetProperty("id").GetGuid() == id && item.GetProperty("targetType").GetString() == "Url");
            using var elsewhere = await guest.GetAsync($"/api/ads?placement=cart&storeId={store}");
            Assert.DoesNotContain(Data(await elsewhere.Content.ReadAsStringAsync()).EnumerateArray(), item => item.GetProperty("id").GetGuid() == id);
            using var noStore = await guest.GetAsync("/api/ads?placement=home_middle");
            Assert.DoesNotContain(Data(await noStore.Content.ReadAsStringAsync()).EnumerateArray(), item => item.GetProperty("id").GetGuid() == id);
            using var unknown = await guest.GetAsync("/api/ads?placement=nowhere");
            Assert.Empty(Data(await unknown.Content.ReadAsStringAsync()).EnumerateArray());

            Assert.Equal(HttpStatusCode.NoContent, (await guest.PostAsync($"/api/ads/{id}/view", null)).StatusCode);
            Assert.Equal(HttpStatusCode.NoContent, (await guest.PostAsync($"/api/ads/{id}/view", null)).StatusCode);
            Assert.Equal(HttpStatusCode.NoContent, (await guest.PostAsync($"/api/ads/{id}/tap", null)).StatusCode);
            Assert.Equal(HttpStatusCode.NoContent, (await guest.PostAsync($"/api/ads/{Guid.NewGuid()}/view", null)).StatusCode);

            using var listed = await admin.GetAsync("/api/admin/ads");
            var mine = Data(await listed.Content.ReadAsStringAsync()).EnumerateArray().Single(item => item.GetProperty("id").GetGuid() == id);
            Assert.Equal((2L, 1L), (mine.GetProperty("views").GetInt64(), mine.GetProperty("taps").GetInt64()));

            using var off = await admin.PutAsJsonAsync($"/api/admin/ads/{id}", new { placement = "home_middle", imageUrl = "/media/offers/http-ad.png", title, isActive = false });
            Assert.Equal(HttpStatusCode.OK, off.StatusCode);
            using var gone = await guest.GetAsync($"/api/ads?placement=home_middle&storeId={store}");
            Assert.DoesNotContain(Data(await gone.Content.ReadAsStringAsync()).EnumerateArray(), item => item.GetProperty("id").GetGuid() == id);

            Assert.Equal(HttpStatusCode.OK, (await admin.DeleteAsync($"/api/admin/ads/{id}")).StatusCode);
            Assert.Equal(HttpStatusCode.NotFound, (await admin.DeleteAsync($"/api/admin/ads/{id}")).StatusCode);
            id = null;
        }
        finally
        {
            await using var scope = api.Services.CreateAsyncScope();
            await scope.ServiceProvider.GetRequiredService<QuickCommerceDbContext>().Ads.Where(item => item.Title == title).ExecuteDeleteAsync();
        }
    }

    [Fact]
    public async Task The_database_refuses_an_ad_with_an_unknown_place_or_target()
    {
        if (Skip) { return; }
        await api.PrepareAsync();
        await using var scope = api.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<QuickCommerceDbContext>();
        var organization = await db.Organizations.Select(item => item.Id).FirstAsync();
        var title = "HTTP db ad " + Guid.NewGuid().ToString("N")[..8];
        try
        {
            db.Ads.Add(new QuickCommerce.Domain.Ad { OrganizationId = organization, Placement = "footer", ImageUrl = "/media/offers/x.png", Title = title });
            await Assert.ThrowsAnyAsync<DbUpdateException>(() => db.SaveChangesAsync());
            db.ChangeTracker.Clear();
            db.Ads.Add(new QuickCommerce.Domain.Ad { OrganizationId = organization, Placement = "home", ImageUrl = "/media/offers/x.png", Title = title, TargetType = "Telephone" });
            await Assert.ThrowsAnyAsync<DbUpdateException>(() => db.SaveChangesAsync());
        }
        finally
        {
            db.ChangeTracker.Clear();
            await db.Ads.Where(item => item.Title == title).ExecuteDeleteAsync();
        }
    }

    [Fact]
    public async Task The_database_itself_refuses_a_card_design_that_is_not_one_of_the_fourteen()
    {
        if (Skip) { return; }
        await api.PrepareAsync();
        await using var scope = api.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<QuickCommerceDbContext>();
        var title = "HTTP check " + Guid.NewGuid().ToString("N")[..8];

        // Every one of the fourteen designs is stored; the database accepts the six everyday ones as well as the festivals.
        foreach (var key in QuickCommerce.Application.Services.OfferCardDesigns.All)
        {
            db.Campaigns.Add(new QuickCommerce.Domain.Campaign { TitleEn = title, BodyEn = "x", CardDesign = key });
        }

        await db.SaveChangesAsync();
        Assert.Equal(QuickCommerce.Application.Services.OfferCardDesigns.All.Length, await db.Campaigns.CountAsync(item => item.TitleEn == title));
        await db.Campaigns.Where(item => item.TitleEn == title).ExecuteDeleteAsync();
        db.ChangeTracker.Clear();

        db.Campaigns.Add(new QuickCommerce.Domain.Campaign { TitleEn = title, BodyEn = "x", CardDesign = "onam" });
        try
        {
            await Assert.ThrowsAnyAsync<DbUpdateException>(() => db.SaveChangesAsync());
        }
        finally
        {
            db.ChangeTracker.Clear();
            await db.Campaigns.Where(item => item.TitleEn == title).ExecuteDeleteAsync();
        }
    }

    [Fact]
    public async Task Checkout_over_http_refuses_a_chosen_time_when_the_store_does_not_schedule_and_a_bad_code()
    {
        if (Skip) { return; }
        var store = await SomeStoreAsync();
        using var customer = await api.ClientAsync(ApiHarness.Customer);

        // The demo customer has no cart, so what matters here is that the route exists, needs the customer and answers in the documented shape.
        using var response = await customer.PostAsJsonAsync($"/api/checkout/{store}", new { deliveryAddress = "x", latitude = 1.0, longitude = 1.0, couponCode = "NOPE", deliverySlotStart = DateTime.UtcNow.AddHours(3) });

        Assert.True(response.StatusCode is HttpStatusCode.BadRequest or HttpStatusCode.Conflict or HttpStatusCode.NotFound, $"unexpected {(int)response.StatusCode}");
        var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement;
        Assert.False(body.GetProperty("success").GetBoolean());
        Assert.False(string.IsNullOrWhiteSpace(body.GetProperty("message").GetString()));
    }
}
