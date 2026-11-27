using QuickCommerce.Application.DTOs;
using QuickCommerce.Application.Interfaces;
using QuickCommerce.Application.Services;
using QuickCommerce.Domain;
using QuickCommerce.Infrastructure;
using Xunit;

namespace QuickCommerce.Tests;

public sealed class PortalAlertTests
{
    private static readonly DateTime Now = DateTime.UtcNow;
    private readonly InMemoryCommerceStore data = new();
    private readonly Guid organization = Guid.NewGuid();
    private readonly Guid mine = Guid.NewGuid();
    private readonly Guid theirs = Guid.NewGuid();
    private readonly List<Coupon> coupons = [];
    private readonly List<Ad> ads = [];

    private PortalAlertService For(bool admin, StaffCategory? category = null)
    {
        var scope = new AuthorizationScope(Guid.NewGuid(), organization, new HashSet<string> { admin ? "ApplicationAdmin" : "StoreStaff" }, new HashSet<Guid>(), new HashSet<string>());
        var context = new UserContext(scope.UserId, organization, mine, admin ? Role.Admin : Role.StoreStaff, category);
        return new PortalAlertService(new Coupons(coupons), data, new Ads(ads), new Scope(scope), new Context(context));
    }

    private void Seed()
    {
        coupons.Add(new Coupon { OrganizationId = organization, Code = "SOON", Kind = CouponKinds.Flat, EndsAt = Now.AddDays(1) });
        coupons.Add(new Coupon { OrganizationId = organization, Code = "LATER", Kind = CouponKinds.Flat, EndsAt = Now.AddDays(10) });
        coupons.Add(new Coupon { OrganizationId = organization, Code = "SWITCHEDOFF", Kind = CouponKinds.Flat, EndsAt = Now.AddDays(1), IsActive = false });
        data.Campaigns.Add(new Campaign { TitleEn = "My offer", BodyEn = "", EndsAt = Now.AddDays(2), Stores = [new CampaignStore { StoreId = mine }] });
        data.Campaigns.Add(new Campaign { TitleEn = "Their offer", BodyEn = "", EndsAt = Now.AddDays(2), Stores = [new CampaignStore { StoreId = theirs }] });
        ads.Add(new Ad { OrganizationId = organization, Placement = "home", ImageUrl = "x", Title = "Old ad", EndsAt = Now.AddDays(-1) });
        ads.Add(new Ad { OrganizationId = organization, Placement = "home", ImageUrl = "x", Title = "Long gone", EndsAt = Now.AddDays(-9) });
        ads.Add(new Ad { OrganizationId = organization, Placement = "home", ImageUrl = "x", Title = "Their ad", StoreId = theirs, EndsAt = Now.AddDays(1) });
    }

    [Fact]
    public async Task An_administrator_is_told_of_everything_ending_soon_or_just_ended()
    {
        Seed();
        var found = (await For(admin: true).GetAsync())!;

        Assert.Equal(["My offer", "Old ad", "SOON", "Their ad", "Their offer"], found.Select(item => item.Name).Order(StringComparer.Ordinal));
        // The ones that already ended come last.
        Assert.Equal("Old ad", found[^1].Name);
        Assert.True(found[^1].Ended);
    }

    [Fact]
    public async Task A_store_manager_is_told_of_their_stores_offers_and_ads_only()
    {
        Seed();
        var found = (await For(admin: false, StaffCategory.StoreManager).GetAsync())!;

        Assert.Equal(["My offer", "Old ad"], found.Select(item => item.Name));
    }

    [Fact]
    public async Task Other_staff_get_no_alerts() => Assert.Null(await For(admin: false, StaffCategory.StoreEmployee).GetAsync());

    private sealed class Scope(AuthorizationScope scope) : IAuthorizationScopeService
    {
        public Task<AuthorizationScope?> ResolveAsync(CancellationToken cancellationToken = default) => Task.FromResult<AuthorizationScope?>(scope);
        public Task<AuthorizationScope?> ResolveForUserAsync(Guid userId, CancellationToken cancellationToken = default) => Task.FromResult<AuthorizationScope?>(scope);
        public Task<bool> HasPermissionAsync(string permissionCode, CancellationToken cancellationToken = default) => Task.FromResult(true);
        public Task<bool> CanAccessStoreAsync(Guid storeId, CancellationToken cancellationToken = default) => Task.FromResult(true);
        public Task<bool> OwnsCustomerAsync(Guid customerId, CancellationToken cancellationToken = default) => Task.FromResult(false);
    }

    private sealed class Context(UserContext context) : ICurrentUserContextResolver
    {
        public Task<UserContext?> ResolveAsync(CancellationToken cancellationToken = default) => Task.FromResult<UserContext?>(context);
    }

    private sealed class Coupons(List<Coupon> list) : ICouponStore
    {
        public Task<IReadOnlyList<CouponSummary>> ListAsync(Guid organizationId, CancellationToken cancellationToken = default) => Task.FromResult<IReadOnlyList<CouponSummary>>(list.Select(item => new CouponSummary(item, 0)).ToArray());
        public Task<Coupon?> GetByCodeAsync(Guid organizationId, string code, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<Coupon?> GetAsync(Guid organizationId, Guid id, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<bool> AddAsync(Coupon coupon, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<bool> SaveAsync(Coupon coupon, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<Guid?> GetStoreOrganizationAsync(Guid storeId, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<IReadOnlyList<Coupon>> ListShownAsync(Guid organizationId, DateTime now, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<int> UsesByUserAsync(Guid couponId, Guid userId, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<bool> HasOrdersAsync(Guid userId, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }

    private sealed class Ads(List<Ad> list) : IAdStore
    {
        public Task<IReadOnlyList<Ad>> ListAsync(Guid organizationId, CancellationToken cancellationToken = default) => Task.FromResult<IReadOnlyList<Ad>>(list);
        public Task<Ad?> GetAsync(Guid organizationId, Guid id, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task AddAsync(Ad ad, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task SaveAsync(Ad ad, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<bool> DeleteAsync(Guid organizationId, Guid id, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<bool> StoreBelongsAsync(Guid organizationId, Guid storeId, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<IReadOnlyList<Ad>> LiveAsync(string placement, Guid? storeId, DateTime now, int take, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<bool> CountViewAsync(Guid id, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<bool> CountTapAsync(Guid id, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }
}
