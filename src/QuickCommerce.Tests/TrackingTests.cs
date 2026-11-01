using Microsoft.EntityFrameworkCore;
using QuickCommerce.Application.DTOs;
using QuickCommerce.Application.Interfaces;
using QuickCommerce.Application;
using QuickCommerce.Application.Services;
using QuickCommerce.Domain;
using QuickCommerce.Infrastructure;
using QuickCommerce.Infrastructure.Persistence;
using Xunit;

namespace QuickCommerce.Tests;

/// <summary>P23: delivery tracking. The partner shares a position; the customer sees distance and ETA, never coordinates.</summary>
[Collection("SqlServer")]
public sealed class TrackingTests
{
    private sealed class Who : ICurrentUserContextResolver
    {
        public UserContext? Context { get; set; }
        public Task<UserContext?> ResolveAsync(CancellationToken cancellationToken = default) => Task.FromResult(Context);
    }

    private sealed class MemoryTracking(InMemoryCommerceStore data) : ITrackingStore
    {
        public Dictionary<Guid, DeliveryLocation> Rows { get; } = [];

        public Task<TrackedOrder?> GetOrderAsync(Guid orderId, CancellationToken cancellationToken = default) =>
            Task.FromResult(data.Orders.Where(order => order.Id == orderId).Select(order => new TrackedOrder(order.Id, order.UserId, order.DeliveryPartnerId, order.Status, order.Latitude, order.Longitude)).FirstOrDefault());

        public Task SaveLocationAsync(Guid orderId, Guid partnerId, double latitude, double longitude, DateTime now, CancellationToken cancellationToken = default)
        {
            Rows[orderId] = new DeliveryLocation { OrderId = orderId, PartnerId = partnerId, Latitude = latitude, Longitude = longitude, UpdatedAt = now };
            return Task.CompletedTask;
        }

        public Task<DeliveryLocation?> GetLocationAsync(Guid orderId, CancellationToken cancellationToken = default) => Task.FromResult(Rows.GetValueOrDefault(orderId));

        public Task DeleteLocationAsync(Guid orderId, CancellationToken cancellationToken = default)
        {
            Rows.Remove(orderId);
            return Task.CompletedTask;
        }
    }

    private sealed class Rig
    {
        public required InMemoryCommerceStore Data { get; init; }
        public required MemoryTracking Store { get; init; }
        public required Who Who { get; init; }
        public required TestClock Clock { get; init; }
        public required TrackingService Service { get; init; }
        public required Order Order { get; init; }
        public required Guid Partner { get; init; }
        public required Guid Customer { get; init; }
        public Guid Org => Data.Organizations.Single().Id;

        public void SignIn(Guid user, Role role) => Who.Context = new UserContext(user, Org, null, role);
    }

    // The order is delivered to (19.0, 73.0). One degree of latitude is about 111 km, so 0.045 degrees is about 5 km.
    private static Rig Create(OrderStatus status = OrderStatus.OutForDelivery)
    {
        var data = InMemoryCommerceStore.CreateSeeded();
        var customer = data.Users.Single().Id;
        var partner = Guid.NewGuid();
        data.Users.Add(new User { Id = partner, ExternalSubject = "tracker", DisplayName = "Rina Kale", FirstName = "Rina", Role = Role.DeliveryPartner, OrganizationId = data.Organizations.Single().Id });
        var order = new Order { OrderNumber = "T-1", UserId = customer, StoreId = data.Stores.First().Id, DeliveryAddress = "x", Latitude = 19.0, Longitude = 73.0, TotalAmount = 50, Status = status, DeliveryPartnerId = partner };
        data.Orders.Add(order);
        var who = new Who();
        var clock = new TestClock(new DateTimeOffset(2026, 10, 17, 10, 0, 0, TimeSpan.Zero));
        var store = new MemoryTracking(data);
        return new Rig { Data = data, Store = store, Who = who, Clock = clock, Service = new TrackingService(store, who, data, clock), Order = order, Partner = partner, Customer = customer };
    }

    [Theory]
    [InlineData(0.05, 1)]
    [InlineData(5, 15)]
    [InlineData(20, 60)]
    [InlineData(7.7, 24)]
    public void The_ETA_is_the_distance_at_a_steady_20_kmh_rounded_up_and_never_under_a_minute(double km, int minutes) =>
        Assert.Equal(minutes, TrackingMath.EtaMinutes(km));

    [Fact]
    public async Task The_partner_shares_and_the_customer_sees_distance_and_eta_but_never_coordinates()
    {
        var rig = Create();
        rig.SignIn(rig.Partner, Role.DeliveryPartner);
        var shared = await rig.Service.ShareAsync(rig.Order.Id, new ShareLocationRequest(19.045, 73.0));

        rig.SignIn(rig.Customer, Role.Customer);
        var seen = (await rig.Service.GetAsync(rig.Order.Id)).Value!;

        Assert.Equal(TrackingStatus.Succeeded, shared.Status);
        Assert.True(seen.Available);
        Assert.InRange(seen.DistanceKm!.Value, 4.9, 5.1);
        Assert.Equal(15, seen.EtaMinutes);
        Assert.Equal("Rina", seen.PartnerName);
        Assert.DoesNotContain(typeof(TrackingResponse).GetProperties(), property => property.Name.Contains("Latitude") || property.Name.Contains("Longitude"));
    }

    [Fact]
    public async Task A_new_position_replaces_the_old_one_so_no_route_is_kept()
    {
        var rig = Create();
        rig.SignIn(rig.Partner, Role.DeliveryPartner);

        await rig.Service.ShareAsync(rig.Order.Id, new ShareLocationRequest(19.09, 73.0));
        await rig.Service.ShareAsync(rig.Order.Id, new ShareLocationRequest(19.045, 73.0));

        Assert.Equal(19.045, Assert.Single(rig.Store.Rows).Value.Latitude);
    }

    [Fact]
    public async Task Only_the_assigned_partner_can_share_and_only_a_partner()
    {
        var rig = Create();
        var other = Guid.NewGuid();

        rig.SignIn(other, Role.DeliveryPartner);
        Assert.Equal(TrackingStatus.NotFound, (await rig.Service.ShareAsync(rig.Order.Id, new ShareLocationRequest(19.1, 73))).Status);
        rig.SignIn(rig.Customer, Role.Customer);
        Assert.Equal(TrackingStatus.Forbidden, (await rig.Service.ShareAsync(rig.Order.Id, new ShareLocationRequest(19.1, 73))).Status);
        rig.Who.Context = null;
        Assert.Equal(TrackingStatus.Forbidden, (await rig.Service.ShareAsync(rig.Order.Id, new ShareLocationRequest(19.1, 73))).Status);
        Assert.Empty(rig.Store.Rows);
    }

    [Theory]
    [InlineData(91, 0)]
    [InlineData(-91, 0)]
    [InlineData(0, 181)]
    [InlineData(double.NaN, 0)]
    [InlineData(0, double.PositiveInfinity)]
    public async Task A_position_outside_the_earth_is_refused(double latitude, double longitude)
    {
        var rig = Create();
        rig.SignIn(rig.Partner, Role.DeliveryPartner);

        Assert.Equal(TrackingStatus.Invalid, (await rig.Service.ShareAsync(rig.Order.Id, new ShareLocationRequest(latitude, longitude))).Status);
        Assert.Empty(rig.Store.Rows);
    }

    [Theory]
    [InlineData(OrderStatus.Ready)]
    [InlineData(OrderStatus.Delivered)]
    [InlineData(OrderStatus.Cancelled)]
    public async Task Sharing_is_refused_and_the_position_erased_when_the_order_is_not_out_for_delivery(OrderStatus status)
    {
        var rig = Create(OrderStatus.OutForDelivery);
        rig.SignIn(rig.Partner, Role.DeliveryPartner);
        await rig.Service.ShareAsync(rig.Order.Id, new ShareLocationRequest(19.045, 73.0));
        rig.Order.Status = status;

        var result = await rig.Service.ShareAsync(rig.Order.Id, new ShareLocationRequest(19.045, 73.0));

        Assert.Equal(TrackingStatus.WrongState, result.Status);
        Assert.Empty(rig.Store.Rows);
    }

    [Fact]
    public async Task The_customer_of_another_order_cannot_see_it()
    {
        var rig = Create();
        rig.SignIn(rig.Partner, Role.DeliveryPartner);
        await rig.Service.ShareAsync(rig.Order.Id, new ShareLocationRequest(19.045, 73.0));

        rig.SignIn(Guid.NewGuid(), Role.Customer);
        Assert.Equal(TrackingStatus.NotFound, (await rig.Service.GetAsync(rig.Order.Id)).Status);
        rig.SignIn(rig.Partner, Role.DeliveryPartner);
        Assert.Equal(TrackingStatus.Forbidden, (await rig.Service.GetAsync(rig.Order.Id)).Status);
    }

    [Fact]
    public async Task Nothing_is_shown_before_the_first_position_when_it_is_stale_or_after_the_delivery_and_the_row_goes()
    {
        var rig = Create();
        rig.SignIn(rig.Customer, Role.Customer);
        Assert.False((await rig.Service.GetAsync(rig.Order.Id)).Value!.Available);

        rig.SignIn(rig.Partner, Role.DeliveryPartner);
        await rig.Service.ShareAsync(rig.Order.Id, new ShareLocationRequest(19.045, 73.0));
        rig.SignIn(rig.Customer, Role.Customer);
        Assert.True((await rig.Service.GetAsync(rig.Order.Id)).Value!.Available);
        rig.Clock.Advance(TimeSpan.FromMinutes(6));
        Assert.False((await rig.Service.GetAsync(rig.Order.Id)).Value!.Available);

        rig.Clock.Advance(TimeSpan.FromMinutes(-6));
        rig.Order.Status = OrderStatus.Delivered;
        Assert.False((await rig.Service.GetAsync(rig.Order.Id)).Value!.Available);
        Assert.Empty(rig.Store.Rows);
    }

    [Fact]
    public async Task On_SQL_one_row_per_order_is_replaced_old_rows_are_purged_the_database_refuses_nonsense_and_delete_works()
    {
        if (CheckoutEfIntegrationTests.ConnectionString is null)
        {
            return;
        }

        await using var world = await CheckoutEfIntegrationTests.World.CreateAsync(1, 5);
        var partner = Guid.NewGuid();
        Guid orderId;
        await using (var db = world.Db())
        {
            var organization = await db.Stores.Where(store => store.Id == world.Store.Id).Select(store => store.OrganizationId).SingleAsync();
            db.Users.Add(new User { Id = partner, ExternalSubject = "trk" + world.Token, DisplayName = "Tracker", Role = Role.DeliveryPartner, OrganizationId = organization });
            await db.SaveChangesAsync();
        }

        var placed = await world.Checkout(world.CustomerIds[0], CheckoutEfIntegrationTests.Commit(null));
        orderId = placed.Order!.Id;
        try
        {
            await using (var db = world.Db())
            {
                var store = new EfTrackingStore(db);
                var now = DateTime.UtcNow;
                await store.SaveLocationAsync(orderId, partner, 19.0, 73.0, now);
                await store.SaveLocationAsync(orderId, partner, 19.1, 73.1, now.AddSeconds(20));
                Assert.Equal(1, await db.DeliveryLocations.CountAsync(location => location.OrderId == orderId));
                Assert.Equal(19.1, (await store.GetLocationAsync(orderId))!.Latitude);
                var tracked = await store.GetOrderAsync(orderId);
                Assert.Equal(world.CustomerIds.Count, 1);
                Assert.Equal(OrderStatus.Pending, tracked!.Status);
                await Assert.ThrowsAnyAsync<Exception>(async () => { await db.DeliveryLocations.Where(location => location.OrderId == orderId).ExecuteUpdateAsync(set => set.SetProperty(location => location.Latitude, 200.0)); });
                // a position left behind for more than a day goes at the next save
                await db.DeliveryLocations.Where(location => location.OrderId == orderId).ExecuteUpdateAsync(set => set.SetProperty(location => location.UpdatedAt, now.AddDays(-3)));
                await store.SaveLocationAsync(Guid.NewGuid(), partner, 1, 1, now).ContinueWith(_ => { });
                Assert.Equal(0, await db.DeliveryLocations.CountAsync(location => location.OrderId == orderId));
                await store.SaveLocationAsync(orderId, partner, 19.0, 73.0, now);
                await store.DeleteLocationAsync(orderId);
                Assert.Null(await store.GetLocationAsync(orderId));
            }
        }
        finally
        {
            await using var cleanup = world.Db();
            await cleanup.DeliveryLocations.Where(location => location.OrderId == orderId).ExecuteDeleteAsync();
            await cleanup.Users.Where(user => user.Id == partner).ExecuteDeleteAsync();
        }
    }
}
