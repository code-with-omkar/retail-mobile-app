using Microsoft.EntityFrameworkCore;
using QuickCommerce.Application.DTOs;
using QuickCommerce.Application.Interfaces;
using QuickCommerce.Application.Services;
using QuickCommerce.Domain;
using QuickCommerce.Infrastructure;
using QuickCommerce.Infrastructure.Persistence;
using Xunit;

namespace QuickCommerce.Tests;

/// <summary>Phase P13: offers belong to stores. Store staff announce for their own store; an administrator picks the stores; customers of those stores get them.</summary>
public sealed class CampaignStoreTests
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
        public required Store A { get; init; }
        public required Store B { get; init; }
        public required Store C { get; init; }
        public required CampaignAdminService Admin { get; init; }
        public required CampaignAdminService StaffOfA { get; init; }
        public required CampaignAdminService StaffOfB { get; init; }
    }

    private static Rig Create()
    {
        var data = InMemoryCommerceStore.CreateSeeded();
        var stores = data.Stores.Take(3).ToArray();
        var organization = data.Organizations.Single().Id;
        var user = data.Users.Single().Id;
        CampaignAdminService Staff(Store store) => new(data, new FixedScope(false, organization), new FixedUser(new UserContext(user, organization, store.Id, Role.StoreStaff)));
        return new Rig
        {
            Data = data,
            A = stores[0],
            B = stores[1],
            C = stores[2],
            Admin = new CampaignAdminService(data, new FixedScope(true, organization), new FixedUser(null)),
            StaffOfA = Staff(stores[0]),
            StaffOfB = Staff(stores[1])
        };
    }

    private static CampaignRequest Offer(string title = "Fruit day", params Guid[] stores) => new(title, "20% off all fruit.", StoreIds: stores);

    // ---------------- who may create, see and cancel ----------------

    [Fact]
    public async Task Store_staff_announce_for_their_own_store_whatever_they_send()
    {
        var rig = Create();

        var empty = await rig.StaffOfA.CreateAsync(Offer());
        var own = await rig.StaffOfA.CreateAsync(Offer(stores: rig.A.Id));

        Assert.All(new[] { empty, own }, result =>
        {
            Assert.Equal(CampaignOperationStatus.Succeeded, result.Status);
            Assert.Equal([rig.A.Id], result.Value!.Stores.Select(store => store.Id));
            Assert.False(result.Value.AllStores);
        });
        Assert.Equal(rig.A.Name, empty.Value!.Stores.Single().Name);
    }

    [Fact]
    public async Task Store_staff_cannot_announce_for_another_store_or_for_several()
    {
        var rig = Create();

        Assert.Equal(CampaignOperationStatus.Unauthorized, (await rig.StaffOfA.CreateAsync(Offer(stores: rig.B.Id))).Status);
        Assert.Equal(CampaignOperationStatus.Unauthorized, (await rig.StaffOfA.CreateAsync(Offer(stores: [rig.A.Id, rig.B.Id]))).Status);
        Assert.Empty(rig.Data.Campaigns);
    }

    [Fact]
    public async Task An_administrator_picks_the_stores_and_no_store_means_every_store()
    {
        var rig = Create();

        var some = (await rig.Admin.CreateAsync(Offer("Two stores", rig.A.Id, rig.B.Id))).Value!;
        var all = (await rig.Admin.CreateAsync(Offer("Everyone"))).Value!;

        Assert.Equal(new[] { rig.A.Id, rig.B.Id }.Order(), some.Stores.Select(store => store.Id).Order());
        Assert.False(some.AllStores);
        Assert.True(all.AllStores);
        Assert.Empty(all.Stores);
    }

    [Fact]
    public async Task An_unknown_store_is_refused()
    {
        var rig = Create();

        var result = await rig.Admin.CreateAsync(Offer("x", Guid.NewGuid()));

        Assert.Equal(CampaignOperationStatus.InvalidRequest, result.Status);
        Assert.Empty(rig.Data.Campaigns);
    }

    [Fact]
    public async Task Store_staff_see_only_the_offers_that_are_for_their_store()
    {
        var rig = Create();
        await rig.Admin.CreateAsync(Offer("Only B", rig.B.Id));
        await rig.Admin.CreateAsync(Offer("A and B", rig.A.Id, rig.B.Id));
        await rig.Admin.CreateAsync(Offer("Everyone"));
        await rig.StaffOfA.CreateAsync(Offer("A own"));
        await rig.StaffOfB.CreateAsync(Offer("B own"));

        var seenByA = (await rig.StaffOfA.ListAsync()).Value!.Select(item => item.TitleEn).Order().ToArray();
        var seenByAdmin = (await rig.Admin.ListAsync()).Value!;

        Assert.Equal(["A and B", "A own"], seenByA);
        Assert.Equal(5, seenByAdmin.Count);
    }

    [Fact]
    public async Task Store_staff_cancel_only_the_offers_that_are_for_their_store_alone_and_an_administrator_cancels_any()
    {
        var rig = Create();
        var own = (await rig.StaffOfA.CreateAsync(Offer("A own"))).Value!;
        var shared = (await rig.Admin.CreateAsync(Offer("A and B", rig.A.Id, rig.B.Id))).Value!;
        var everyone = (await rig.Admin.CreateAsync(Offer("Everyone"))).Value!;
        var theirs = (await rig.StaffOfB.CreateAsync(Offer("B own"))).Value!;

        Assert.Equal(CampaignStatuses.Cancelled, (await rig.StaffOfA.CancelAsync(own.Id)).Value!.Status);
        Assert.Equal(CampaignOperationStatus.NotFound, (await rig.StaffOfA.CancelAsync(shared.Id)).Status);
        Assert.Equal(CampaignOperationStatus.NotFound, (await rig.StaffOfA.CancelAsync(everyone.Id)).Status);
        Assert.Equal(CampaignOperationStatus.NotFound, (await rig.StaffOfA.CancelAsync(theirs.Id)).Status);
        Assert.Equal(CampaignStatuses.Cancelled, (await rig.Admin.CancelAsync(shared.Id)).Value!.Status);
        Assert.Equal(CampaignStatuses.Scheduled, (await rig.Admin.ListAsync()).Value!.Single(item => item.Id == theirs.Id).Status);
    }

    [Fact]
    public async Task Someone_who_is_neither_an_administrator_nor_store_staff_can_do_nothing()
    {
        var rig = Create();
        var organization = rig.Data.Organizations.Single().Id;
        var customer = new CampaignAdminService(rig.Data, new FixedScope(false, organization), new FixedUser(new UserContext(Guid.NewGuid(), organization, null, Role.Customer)));
        var staffWithoutStore = new CampaignAdminService(rig.Data, new FixedScope(false, organization), new FixedUser(new UserContext(Guid.NewGuid(), organization, null, Role.StoreStaff)));

        foreach (var service in new[] { customer, staffWithoutStore })
        {
            Assert.Equal(CampaignOperationStatus.Unauthorized, (await service.CreateAsync(Offer())).Status);
            Assert.Equal(CampaignOperationStatus.Unauthorized, (await service.ListAsync()).Status);
            Assert.Equal(CampaignOperationStatus.Unauthorized, (await service.CancelAsync(Guid.NewGuid())).Status);
        }
    }

    // ---------------- who receives it ----------------

    private static Customer AddCustomer(InMemoryCommerceStore data, bool offers = true)
    {
        var customer = new Customer { UserId = Guid.NewGuid(), OffersEnabled = offers };
        data.Customers.Add(customer);
        return customer;
    }

    [Fact]
    public async Task An_offer_for_a_store_reaches_that_stores_customers_with_offers_on_and_names_the_store()
    {
        var rig = Create();
        var orderer = AddCustomer(rig.Data);
        var shopper = AddCustomer(rig.Data);
        var stranger = AddCustomer(rig.Data);
        var otherStore = AddCustomer(rig.Data);
        var optedOut = AddCustomer(rig.Data, offers: false);
        rig.Data.Orders.Add(new Order { OrderNumber = "x1", UserId = orderer.UserId, StoreId = rig.A.Id, DeliveryAddress = "a", TotalAmount = 1, Items = [] });
        rig.Data.Carts.Add(new Cart { CustomerId = shopper.Id, StoreId = rig.A.Id });
        rig.Data.Orders.Add(new Order { OrderNumber = "x2", UserId = otherStore.UserId, StoreId = rig.B.Id, DeliveryAddress = "a", TotalAmount = 1, Items = [] });
        rig.Data.Orders.Add(new Order { OrderNumber = "x3", UserId = optedOut.UserId, StoreId = rig.A.Id, DeliveryAddress = "a", TotalAmount = 1, Items = [] });
        await rig.StaffOfA.CreateAsync(Offer("A only"));

        await rig.Data.PublishDueCampaignsAsync(DateTime.UtcNow.AddMinutes(1));

        var got = rig.Data.Notifications.Where(item => item.Category == NotificationCategories.Offer).Select(item => item.CustomerId).ToHashSet();
        Assert.Equal(new[] { orderer.Id, shopper.Id }.ToHashSet(), got);
        Assert.DoesNotContain(stranger.Id, got);
        Assert.DoesNotContain(otherStore.Id, got);
        Assert.DoesNotContain(optedOut.Id, got);
        Assert.All(rig.Data.Notifications, note => Assert.Equal(rig.A.Name, NotificationCatalog.ReadStore(note.DataJson)));
        Assert.Equal(2, (await rig.Admin.ListAsync()).Value!.Single().RecipientCount);
    }

    [Fact]
    public async Task An_offer_for_every_store_reaches_every_customer_with_offers_on_and_names_no_store()
    {
        var rig = Create();
        var first = AddCustomer(rig.Data);
        var second = AddCustomer(rig.Data);
        await rig.Admin.CreateAsync(Offer("Everyone"));

        await rig.Data.PublishDueCampaignsAsync(DateTime.UtcNow.AddMinutes(1));

        var got = rig.Data.Notifications.Select(item => item.CustomerId).ToHashSet();
        Assert.Contains(first.Id, got);
        Assert.Contains(second.Id, got);
        Assert.All(rig.Data.Notifications, note => Assert.Null(NotificationCatalog.ReadStore(note.DataJson)));
    }

    [Fact]
    public async Task A_customer_of_two_of_the_chosen_stores_gets_one_offer_naming_the_first_by_name()
    {
        var rig = Create();
        var both = AddCustomer(rig.Data);
        rig.Data.Orders.Add(new Order { OrderNumber = "y1", UserId = both.UserId, StoreId = rig.A.Id, DeliveryAddress = "a", TotalAmount = 1, Items = [] });
        rig.Data.Orders.Add(new Order { OrderNumber = "y2", UserId = both.UserId, StoreId = rig.B.Id, DeliveryAddress = "a", TotalAmount = 1, Items = [] });
        await rig.Admin.CreateAsync(Offer("Two", rig.A.Id, rig.B.Id));

        await rig.Data.PublishDueCampaignsAsync(DateTime.UtcNow.AddMinutes(1));

        var note = Assert.Single(rig.Data.Notifications, item => item.CustomerId == both.Id);
        Assert.Equal(new[] { rig.A.Name, rig.B.Name }.Order().First(), NotificationCatalog.ReadStore(note.DataJson));
    }
}

/// <summary>The same audience rules on SQL Server.</summary>
[Collection("SqlServer")]
public sealed class CampaignStoreEfIntegrationTests
{
    private static string? ConnectionString => CheckoutEfIntegrationTests.ConnectionString;

    private static async Task<Guid> AddAsync(CheckoutEfIntegrationTests.World world, string title, params Guid[] storeIds)
    {
        await using var db = world.Db();
        var campaign = new Campaign { TitleEn = title, BodyEn = "body", StartsAt = DateTime.UtcNow.AddMinutes(-1), CreatedBy = "test", Stores = storeIds.Select(id => new CampaignStore { StoreId = id }).ToList() };
        db.Campaigns.Add(campaign);
        await db.SaveChangesAsync();
        return campaign.Id;
    }

    private static async Task CleanAsync(CheckoutEfIntegrationTests.World world, params Guid[] ids)
    {
        await using var db = world.Db();
        await db.Notifications.Where(item => item.CampaignId != null && ids.Contains(item.CampaignId.Value)).ExecuteDeleteAsync();
        await db.Campaigns.Where(item => ids.Contains(item.Id)).ExecuteDeleteAsync();
    }

    [Fact]
    public async Task An_offer_for_a_store_reaches_only_customers_who_ordered_or_shopped_there_and_names_the_store()
    {
        if (ConnectionString is null)
        {
            return;
        }

        await using var first = await CheckoutEfIntegrationTests.World.CreateAsync(customers: 2, stock: 10, cartQuantity: 1);
        await using var second = await CheckoutEfIntegrationTests.World.CreateAsync(customers: 2, stock: 10, cartQuantity: 1);
        // The first store's first customer has ordered; the second only has a cart (the world gave everyone one).
        await first.Checkout(first.CustomerIds[0], CheckoutEfIntegrationTests.Commit(null));
        var id = await AddAsync(first, "Store offer " + Guid.NewGuid().ToString("N")[..6], first.Store.Id);
        try
        {
            await using (var db = first.Db())
            {
                await new EfCampaignStore(db).PublishDueCampaignsAsync(DateTime.UtcNow);
            }

            await using var verify = first.Db();
            var sent = await verify.Notifications.AsNoTracking().Where(item => item.CampaignId == id).ToListAsync();
            Assert.Equal(first.CustomerIds.Order(), sent.Select(item => item.CustomerId).Order());
            Assert.All(sent, note => Assert.Equal(first.Store.Name, NotificationCatalog.ReadStore(note.DataJson)));
            Assert.DoesNotContain(sent, note => second.CustomerIds.Contains(note.CustomerId));
        }
        finally
        {
            await CleanAsync(first, id);
        }
    }

    [Fact]
    public async Task An_offer_for_no_store_in_particular_reaches_every_customer()
    {
        if (ConnectionString is null)
        {
            return;
        }

        await using var first = await CheckoutEfIntegrationTests.World.CreateAsync(customers: 1, stock: 5);
        await using var second = await CheckoutEfIntegrationTests.World.CreateAsync(customers: 1, stock: 5);
        var id = await AddAsync(first, "Everyone " + Guid.NewGuid().ToString("N")[..6]);
        try
        {
            await using (var db = first.Db())
            {
                await new EfCampaignStore(db).PublishDueCampaignsAsync(DateTime.UtcNow);
            }

            await using var verify = first.Db();
            var got = await verify.Notifications.AsNoTracking().Where(item => item.CampaignId == id).Select(item => item.CustomerId).ToListAsync();
            Assert.Contains(first.CustomerIds[0], got);
            Assert.Contains(second.CustomerIds[0], got);
        }
        finally
        {
            await CleanAsync(first, id);
        }
    }

    [Fact]
    public async Task The_stores_of_an_offer_are_listed_with_their_names_and_the_database_keeps_one_row_per_store()
    {
        if (ConnectionString is null)
        {
            return;
        }

        await using var world = await CheckoutEfIntegrationTests.World.CreateAsync(customers: 1, stock: 5);
        var id = await AddAsync(world, "Listed " + Guid.NewGuid().ToString("N")[..6], world.Store.Id);
        try
        {
            await using (var db = world.Db())
            {
                var listed = await new EfCampaignStore(db).GetCampaignAsync(id);
                Assert.Equal(world.Store.Name, Assert.Single(listed!.Stores).Store.Name);
            }

            await using var duplicate = world.Db();
            duplicate.CampaignStores.Add(new CampaignStore { CampaignId = id, StoreId = world.Store.Id });
            await Assert.ThrowsAsync<DbUpdateException>(() => duplicate.SaveChangesAsync());
        }
        finally
        {
            await CleanAsync(world, id);
        }
    }
}
