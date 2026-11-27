using Microsoft.EntityFrameworkCore;
using QuickCommerce.Application.DTOs;
using QuickCommerce.Application.Interfaces;
using QuickCommerce.Application.Services;
using QuickCommerce.Domain;
using QuickCommerce.Infrastructure;
using QuickCommerce.Infrastructure.Persistence;
using Xunit;

namespace QuickCommerce.Tests;

/// <summary>P20: the extra fee for an offer that is also shown when the app opens, and the stores and items a customer bought before.</summary>
[Collection("SqlServer")]
public sealed class OfferLoadFeeAndHistoryTests
{
    private sealed class FixedScope(bool admin, Guid organizationId) : IAuthorizationScopeService
    {
        private AuthorizationScope Scope => new(Guid.NewGuid(), organizationId, new HashSet<string> { admin ? "ApplicationAdmin" : "StoreStaff" }, new HashSet<Guid>(), new HashSet<string>());
        public Task<AuthorizationScope?> ResolveAsync(CancellationToken cancellationToken = default) => Task.FromResult<AuthorizationScope?>(Scope);
        public Task<AuthorizationScope?> ResolveForUserAsync(Guid userId, CancellationToken cancellationToken = default) => Task.FromResult<AuthorizationScope?>(Scope);
        public Task<bool> HasPermissionAsync(string permissionCode, CancellationToken cancellationToken = default) => Task.FromResult(admin);
        public Task<bool> CanAccessStoreAsync(Guid storeId, CancellationToken cancellationToken = default) => Task.FromResult(admin);
        public Task<bool> OwnsCustomerAsync(Guid customerId, CancellationToken cancellationToken = default) => Task.FromResult(false);
    }

    private sealed class FixedUser(UserContext? context) : ICurrentUserContextResolver
    {
        public Task<UserContext?> ResolveAsync(CancellationToken cancellationToken = default) => Task.FromResult(context);
    }

    private sealed class Rig
    {
        public required InMemoryCommerceStore Data { get; init; }
        public required Store Harbor { get; init; }
        public required OfferFeeService AdminFees { get; init; }
        public required OfferFeeService StaffFees { get; init; }
        public required CampaignAdminService Admin { get; init; }
        public required CampaignAdminService Staff { get; init; }
    }

    private static Rig Create()
    {
        var data = InMemoryCommerceStore.CreateSeeded();
        var clock = new TestClock(new DateTimeOffset(2026, 10, 15, 10, 0, 0, TimeSpan.Zero));
        var settings = new PaymentSettings { Enabled = true };
        var organization = data.Organizations.Single().Id;
        var user = data.Users.Single().Id;
        var harbor = data.Stores.Single(store => store.Name == "Harbor Point Dark Store");
        var staffUser = new FixedUser(new UserContext(user, organization, harbor.Id, Role.StoreStaff));
        return new Rig
        {
            Data = data,
            Harbor = harbor,
            AdminFees = new OfferFeeService(data, new FixedScope(true, organization), new FixedUser(null), settings, clock),
            StaffFees = new OfferFeeService(data, new FixedScope(false, organization), staffUser, settings, clock),
            Admin = new CampaignAdminService(data, new FixedScope(true, organization), new FixedUser(null), clock, data, settings),
            Staff = new CampaignAdminService(data, new FixedScope(false, organization), staffUser, clock, data, settings)
        };
    }

    [Fact]
    public async Task Only_an_administrator_sets_the_app_open_fee_within_limits()
    {
        var rig = Create();

        Assert.Equal(CampaignOperationStatus.Unauthorized, (await rig.StaffFees.SetLoadAsync(5000)).Status);
        Assert.Equal(CampaignOperationStatus.InvalidRequest, (await rig.AdminFees.SetLoadAsync(-1)).Status);
        Assert.Equal(CampaignOperationStatus.InvalidRequest, (await rig.AdminFees.SetLoadAsync(OfferFeeService.MaxPaise + 1)).Status);
        var set = await rig.AdminFees.SetLoadAsync(15000);

        Assert.Equal(15000, set.Value!.LoadPaise);
        Assert.Equal(15000, (await rig.StaffFees.GetMineAsync()).Value!.LoadPaise);
    }

    [Fact]
    public async Task Staff_pay_the_normal_fee_plus_the_app_open_fee_only_when_they_ask_for_it()
    {
        var rig = Create();
        await rig.AdminFees.SetDefaultAsync(10000);
        await rig.AdminFees.SetLoadAsync(15000);

        var plain = (await rig.Staff.CreateAsync(new CampaignRequest("Sale", "Big sale."))).Value!;
        var popup = (await rig.Staff.CreateAsync(new CampaignRequest("Sale", "Big sale.", ShowOnLoad: true))).Value!;

        Assert.Equal((10000, false), (plain.FeePaise, plain.ShowOnLoad));
        Assert.Equal((25000, true), (popup.FeePaise, popup.ShowOnLoad));
    }

    [Fact]
    public async Task The_app_open_fee_follows_a_stores_own_fee_and_a_free_store_still_pays_it()
    {
        var rig = Create();
        await rig.AdminFees.SetDefaultAsync(10000);
        await rig.AdminFees.SetStoreAsync(rig.Harbor.Id, 0);
        await rig.AdminFees.SetLoadAsync(2000);

        var popup = (await rig.Staff.CreateAsync(new CampaignRequest("Sale", "Big sale.", ShowOnLoad: true))).Value!;

        Assert.Equal(2000, popup.FeePaise);
    }

    [Fact]
    public async Task An_administrators_offer_is_free_even_with_the_app_open_option()
    {
        var rig = Create();
        await rig.AdminFees.SetLoadAsync(15000);

        var offer = (await rig.Admin.CreateAsync(new CampaignRequest("Sale", "Big sale.", ShowOnLoad: true))).Value!;

        Assert.Equal((0, true), (offer.FeePaise, offer.ShowOnLoad));
    }

    [Fact]
    public async Task An_unpaid_app_open_offer_is_not_shown_until_paid()
    {
        var rig = Create();
        await rig.AdminFees.SetLoadAsync(15000);
        await rig.Staff.CreateAsync(new CampaignRequest("Sale", "Big sale.", ShowOnLoad: true, ImageUrl: null));
        var campaign = rig.Data.Campaigns.Single();
        campaign.DiscountPercent = 0;
        campaign.ImageUrl = "/media/offers/a.png";

        Assert.DoesNotContain(await rig.Data.GetActiveOffersAsync(rig.Harbor.Id, DateTime.UtcNow.AddMinutes(1)), offer => offer.ShowOnLoad);
    }

    // ---------------- purchase history on SQL ----------------

    [Fact]
    public async Task Home_stores_and_bought_before_items_come_from_the_customers_own_orders()
    {
        if (CheckoutEfIntegrationTests.ConnectionString is null)
        {
            return;
        }

        await using var world = await CheckoutEfIntegrationTests.World.CreateAsync(2, 20);
        var customer = world.CustomerIds[0];
        var stranger = world.CustomerIds[1];
        Assert.Equal(CheckoutCommitStatus.Succeeded, (await world.Checkout(customer, CheckoutEfIntegrationTests.Commit(null))).Status);
        await world.PutBack(customer);
        Assert.Equal(CheckoutCommitStatus.Succeeded, (await world.Checkout(customer, CheckoutEfIntegrationTests.Commit(null))).Status);
        Guid userId;
        Guid strangerUser;
        await using (var db = world.Db())
        {
            userId = await db.Customers.Where(c => c.Id == customer).Select(c => c.UserId).SingleAsync();
            strangerUser = await db.Customers.Where(c => c.Id == stranger).Select(c => c.UserId).SingleAsync();
        }

        await using (var db = world.Db())
        {
            var history = new EfPurchaseHistoryStore(db);
            var stores = await history.RecentStoresAsync(userId, 4);
            Assert.Equal([world.Store.Id], stores.Select(item => item.Id));
            Assert.Equal(world.Store.Name, stores[0].Name);
            Assert.Equal([world.Product.Id], await history.RecentProductIdsAsync(userId, world.Store.Id, 20));
            Assert.Empty(await history.RecentStoresAsync(strangerUser, 4));
            Assert.Empty(await history.RecentProductIdsAsync(strangerUser, world.Store.Id, 20));
            Assert.Empty(await history.RecentProductIdsAsync(userId, Guid.NewGuid(), 20));
        }

        // A cancelled order is not a purchase; neither is one still waiting for payment.
        await using (var db = world.Db())
        {
            await db.Orders.Where(order => order.StoreId == world.Store.Id).ExecuteUpdateAsync(set => set.SetProperty(order => order.Status, OrderStatus.Cancelled));
        }

        await using (var db = world.Db())
        {
            var history = new EfPurchaseHistoryStore(db);
            Assert.Empty(await history.RecentStoresAsync(userId, 4));
            Assert.Empty(await history.RecentProductIdsAsync(userId, world.Store.Id, 20));
        }

        await using (var db = world.Db())
        {
            await db.Orders.Where(order => order.StoreId == world.Store.Id).ExecuteUpdateAsync(set => set.SetProperty(order => order.Status, OrderStatus.AwaitingPayment));
            Assert.Empty(await new EfPurchaseHistoryStore(db).RecentStoresAsync(userId, 4));
            await db.Orders.Where(order => order.StoreId == world.Store.Id).ExecuteUpdateAsync(set => set.SetProperty(order => order.Status, OrderStatus.Delivered));
            await db.Stores.Where(store => store.Id == world.Store.Id).ExecuteUpdateAsync(set => set.SetProperty(store => store.IsActive, false));
            Assert.Empty(await new EfPurchaseHistoryStore(db).RecentStoresAsync(userId, 4));
        }
    }
}
