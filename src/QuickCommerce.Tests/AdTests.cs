using QuickCommerce.Application.DTOs;
using QuickCommerce.Application.Interfaces;
using QuickCommerce.Application.Services;
using QuickCommerce.Domain;
using Xunit;

namespace QuickCommerce.Tests;

public sealed class AdRulesTests
{
    private static readonly DateTime Now = new(2026, 11, 19, 12, 0, 0, DateTimeKind.Utc);
    private const string Picture = "/media/offers/ad-one.png";

    private static SaveAdRequest Good(Action<Dictionary<string, object?>>? tune = null) => new(AdPlacements.Home, Picture, "Big sale");

    [Fact]
    public void A_good_ad_has_no_problem_and_every_place_and_target_is_known()
    {
        Assert.Null(AdRules.Problem(Good(), Now));
        foreach (var place in AdPlacements.All)
        {
            Assert.Null(AdRules.Problem(Good() with { Placement = place }, Now));
        }

        Assert.Equal(["home", "home_middle", "categories", "search", "cart", "order", "product"], AdPlacements.All);
    }

    [Theory]
    [InlineData("")]
    [InlineData("footer")]
    [InlineData("HOME")]
    public void An_unknown_place_is_refused(string place) => Assert.NotNull(AdRules.Problem(Good() with { Placement = place }, Now));

    [Theory]
    [InlineData("https://evil.example/x.png")]
    [InlineData("/media/offers/../secret.png")]
    [InlineData("/media/other/a.png")]
    [InlineData("")]
    public void Only_a_picture_we_stored_can_be_used(string image) => Assert.NotNull(AdRules.Problem(Good() with { ImageUrl = image }, Now));

    [Fact]
    public void A_title_is_at_most_eighty_letters()
    {
        Assert.Null(AdRules.Problem(Good() with { Title = new string('a', 80) }, Now));
        Assert.NotNull(AdRules.Problem(Good() with { Title = new string('a', 81) }, Now));
        Assert.Null(AdRules.Problem(Good() with { Title = null }, Now));
    }

    [Fact]
    public void A_tap_target_needs_an_id_for_a_product_category_or_offer_and_an_https_link_for_a_web_page()
    {
        var id = Guid.NewGuid().ToString();
        foreach (var type in new[] { AdTargets.Product, AdTargets.Category, AdTargets.Offer })
        {
            Assert.Null(AdRules.Problem(Good() with { TargetType = type, Target = id }, Now));
            Assert.NotNull(AdRules.Problem(Good() with { TargetType = type, Target = "not-an-id" }, Now));
            Assert.NotNull(AdRules.Problem(Good() with { TargetType = type, Target = null }, Now));
        }

        Assert.Null(AdRules.Problem(Good() with { TargetType = AdTargets.Url, Target = "https://example.com/offer?a=1" }, Now));
        Assert.NotNull(AdRules.Problem(Good() with { TargetType = AdTargets.Url, Target = "http://example.com" }, Now));
        Assert.NotNull(AdRules.Problem(Good() with { TargetType = AdTargets.Url, Target = "javascript:alert(1)" }, Now));
        Assert.NotNull(AdRules.Problem(Good() with { TargetType = AdTargets.Url, Target = "https://" + new string('a', 300) + ".com" }, Now));
        Assert.NotNull(AdRules.Problem(Good() with { TargetType = "Telephone" }, Now));
        Assert.Null(AdRules.Problem(Good() with { TargetType = AdTargets.None, Target = "ignored" }, Now));
        Assert.Null(AdRules.NormalizeTarget(AdTargets.None, "ignored"));
        Assert.Equal("abc", AdRules.NormalizeTarget(AdTargets.Url, "  abc "));
    }

    [Fact]
    public void It_must_end_after_it_starts_and_the_order_stays_in_range()
    {
        Assert.NotNull(AdRules.Problem(Good() with { EndsAt = Now }, Now));
        Assert.NotNull(AdRules.Problem(Good() with { EndsAt = Now.AddDays(-1) }, Now));
        Assert.Null(AdRules.Problem(Good() with { EndsAt = Now.AddDays(3) }, Now));
        Assert.NotNull(AdRules.Problem(Good() with { SortOrder = -1 }, Now));
        Assert.NotNull(AdRules.Problem(Good() with { SortOrder = 1001 }, Now));
    }

    [Fact]
    public void An_ad_is_live_between_its_dates_while_on_and_its_status_says_so()
    {
        Ad Make(bool on, DateTime start, DateTime? end) => new() { Placement = AdPlacements.Home, ImageUrl = Picture, IsActive = on, StartsAt = start, EndsAt = end };

        Assert.True(AdRules.IsLive(Make(true, Now.AddDays(-1), null), Now));
        Assert.True(AdRules.IsLive(Make(true, Now, Now.AddDays(1)), Now));
        Assert.False(AdRules.IsLive(Make(true, Now.AddHours(1), null), Now));
        Assert.False(AdRules.IsLive(Make(true, Now.AddDays(-2), Now), Now), "it ends at the end time");
        Assert.False(AdRules.IsLive(Make(false, Now.AddDays(-1), null), Now));

        Assert.Equal("Live", AdRules.Status(Make(true, Now.AddDays(-1), null), Now));
        Assert.Equal("Scheduled", AdRules.Status(Make(true, Now.AddDays(1), null), Now));
        Assert.Equal("Ended", AdRules.Status(Make(true, Now.AddDays(-3), Now.AddDays(-1)), Now));
        Assert.Equal("Off", AdRules.Status(Make(false, Now.AddDays(-1), null), Now));
    }
}

public sealed class AdServiceTests
{
    private static readonly Guid Org = Guid.NewGuid();
    private static readonly Guid Harbor = Guid.NewGuid();
    private static readonly Guid Cedar = Guid.NewGuid();
    private const string Picture = "/media/offers/ad-one.png";

    private sealed class FakeClock(DateTime now) : TimeProvider
    {
        public DateTime Now { get; set; } = now;
        public override DateTimeOffset GetUtcNow() => new(Now, TimeSpan.Zero);
    }

    private sealed class FakeStore : IAdStore
    {
        public readonly List<Ad> Ads = [];
        public Task<IReadOnlyList<Ad>> ListAsync(Guid organizationId, CancellationToken cancellationToken = default) => Task.FromResult<IReadOnlyList<Ad>>(Ads.Where(item => item.OrganizationId == organizationId).ToArray());
        public Task<Ad?> GetAsync(Guid organizationId, Guid id, CancellationToken cancellationToken = default) => Task.FromResult(Ads.FirstOrDefault(item => item.OrganizationId == organizationId && item.Id == id));
        public Task AddAsync(Ad ad, CancellationToken cancellationToken = default) { Ads.Add(ad); return Task.CompletedTask; }
        public Task SaveAsync(Ad ad, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task<bool> DeleteAsync(Guid organizationId, Guid id, CancellationToken cancellationToken = default) => Task.FromResult(Ads.RemoveAll(item => item.OrganizationId == organizationId && item.Id == id) > 0);
        public Task<bool> StoreBelongsAsync(Guid organizationId, Guid storeId, CancellationToken cancellationToken = default) => Task.FromResult(organizationId == Org && (storeId == Harbor || storeId == Cedar));
        public Task<IReadOnlyList<Ad>> LiveAsync(string placement, Guid? storeId, DateTime now, int take, CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<Ad>>(Ads.Where(item => item.Placement == placement && AdRules.IsLive(item, now) && (item.StoreId == null || item.StoreId == storeId)).OrderBy(item => item.SortOrder).Take(take).ToArray());
        public Task<bool> CountViewAsync(Guid id, CancellationToken cancellationToken = default) { var ad = Ads.FirstOrDefault(item => item.Id == id); if (ad is not null) { ad.ViewCount++; } return Task.FromResult(ad is not null); }
        public Task<bool> CountTapAsync(Guid id, CancellationToken cancellationToken = default) { var ad = Ads.FirstOrDefault(item => item.Id == id); if (ad is not null) { ad.ClickCount++; } return Task.FromResult(ad is not null); }
    }

    private sealed class Scope(bool admin) : IAuthorizationScopeService
    {
        private AuthorizationScope Value => new(Guid.NewGuid(), Org, new HashSet<string> { admin ? "ApplicationAdmin" : "StoreStaff" }, new HashSet<Guid>(), new HashSet<string>());
        public Task<AuthorizationScope?> ResolveAsync(CancellationToken cancellationToken = default) => Task.FromResult<AuthorizationScope?>(Value);
        public Task<AuthorizationScope?> ResolveForUserAsync(Guid userId, CancellationToken cancellationToken = default) => Task.FromResult<AuthorizationScope?>(Value);
        public Task<bool> HasPermissionAsync(string permissionCode, CancellationToken cancellationToken = default) => Task.FromResult(admin);
        public Task<bool> CanAccessStoreAsync(Guid storeId, CancellationToken cancellationToken = default) => Task.FromResult(admin);
        public Task<bool> OwnsCustomerAsync(Guid customerId, CancellationToken cancellationToken = default) => Task.FromResult(false);
    }

    private static (AdService Admin, AdService Staff, FakeStore Store, FakeClock Clock) Create()
    {
        var store = new FakeStore();
        var clock = new FakeClock(new DateTime(2026, 11, 19, 12, 0, 0, DateTimeKind.Utc));
        return (new AdService(store, new Scope(true), clock), new AdService(store, new Scope(false), clock), store, clock);
    }

    private static SaveAdRequest Req(string place = AdPlacements.Home, Action<SaveAdRequest>? _ = null) => new(place, Picture, "Sale");

    [Fact]
    public async Task Only_an_administrator_can_list_make_change_or_delete_ads()
    {
        var (admin, staff, _, _) = Create();
        var made = (await admin.CreateAsync(Req())).Value!;

        Assert.Equal(AdServiceStatus.Forbidden, (await staff.ListAsync()).Status);
        Assert.Equal(AdServiceStatus.Forbidden, (await staff.CreateAsync(Req())).Status);
        Assert.Equal(AdServiceStatus.Forbidden, (await staff.UpdateAsync(made.Id, Req())).Status);
        Assert.Equal(AdServiceStatus.Forbidden, (await staff.DeleteAsync(made.Id)).Status);
        Assert.Single((await admin.ListAsync()).Value!);
    }

    [Fact]
    public async Task An_administrator_makes_changes_and_removes_an_ad_and_a_bad_one_is_refused()
    {
        var (admin, _, store, _) = Create();

        var made = await admin.CreateAsync(Req() with { Title = "  Diwali  ", SortOrder = 3, StoreId = Harbor });
        Assert.Equal(AdServiceStatus.Succeeded, made.Status);
        Assert.Equal(("Diwali", "Live", 3, Harbor), (made.Value!.Title, made.Value.Status, made.Value.SortOrder, made.Value.StoreId));

        var changed = await admin.UpdateAsync(made.Value.Id, Req(AdPlacements.Cart) with { IsActive = false, TargetType = AdTargets.Url, Target = "https://example.com" });
        Assert.Equal(("cart", "Off", "https://example.com"), (changed.Value!.Placement, changed.Value.Status, changed.Value.Target));

        Assert.Equal(AdServiceStatus.Invalid, (await admin.CreateAsync(Req("nowhere"))).Status);
        Assert.Equal(AdServiceStatus.Invalid, (await admin.CreateAsync(Req() with { ImageUrl = "https://evil.example/a.png" })).Status);
        Assert.Equal(AdServiceStatus.Invalid, (await admin.CreateAsync(Req() with { StoreId = Guid.NewGuid() })).Status);
        Assert.Equal(AdServiceStatus.NotFound, (await admin.UpdateAsync(Guid.NewGuid(), Req())).Status);

        Assert.Equal(AdServiceStatus.Succeeded, (await admin.DeleteAsync(made.Value.Id)).Status);
        Assert.Equal(AdServiceStatus.NotFound, (await admin.DeleteAsync(made.Value.Id)).Status);
        Assert.Empty(store.Ads);
    }

    [Fact]
    public async Task The_app_gets_the_live_ads_for_a_place_for_all_stores_and_its_own_store_in_order_and_nothing_for_an_unknown_place()
    {
        var (admin, _, _, clock) = Create();
        await admin.CreateAsync(Req(AdPlacements.Home) with { Title = "All stores", SortOrder = 2 });
        await admin.CreateAsync(Req(AdPlacements.Home) with { Title = "Harbor only", StoreId = Harbor, SortOrder = 1 });
        await admin.CreateAsync(Req(AdPlacements.Home) with { Title = "Cedar only", StoreId = Cedar, SortOrder = 0 });
        await admin.CreateAsync(Req(AdPlacements.Home) with { Title = "Later", StartsAt = clock.Now.AddDays(1) });
        await admin.CreateAsync(Req(AdPlacements.Home) with { Title = "Switched off", IsActive = false });
        await admin.CreateAsync(Req(AdPlacements.Cart) with { Title = "In the cart" });

        Assert.Equal(["Harbor only", "All stores"], (await admin.ForPlacementAsync("home", Harbor)).Select(item => item.Title));
        Assert.Equal(["Cedar only", "All stores"], (await admin.ForPlacementAsync("home", Cedar)).Select(item => item.Title));
        Assert.Equal(["All stores"], (await admin.ForPlacementAsync("home", null)).Select(item => item.Title));
        Assert.Equal(["In the cart"], (await admin.ForPlacementAsync("cart", Harbor)).Select(item => item.Title));
        Assert.Empty(await admin.ForPlacementAsync("nowhere", Harbor));
        Assert.Empty(await admin.ForPlacementAsync(null, Harbor));

        clock.Now = clock.Now.AddDays(2);
        Assert.Contains("Later", (await admin.ForPlacementAsync("home", Harbor)).Select(item => item.Title));
    }

    [Fact]
    public async Task Views_and_taps_are_counted_and_an_unknown_ad_changes_nothing()
    {
        var (admin, _, store, _) = Create();
        var made = (await admin.CreateAsync(Req())).Value!;

        await admin.RecordViewAsync(made.Id);
        await admin.RecordViewAsync(made.Id);
        await admin.RecordTapAsync(made.Id);
        await admin.RecordViewAsync(Guid.NewGuid());

        var listed = (await admin.ListAsync()).Value!.Single();
        Assert.Equal((2L, 1L), (listed.Views, listed.Taps));
        Assert.Equal(2, store.Ads.Single().ViewCount);
    }
}
