using Microsoft.EntityFrameworkCore;
using QuickCommerce.Application.DTOs;
using QuickCommerce.Application.Interfaces;
using QuickCommerce.Application.Services;
using QuickCommerce.Domain;
using QuickCommerce.Infrastructure.Persistence;
using Xunit;
using static QuickCommerce.Tests.CheckoutEfIntegrationTests;

namespace QuickCommerce.Tests;

/// <summary>
/// Scheduled delivery against a real SQL Server (opt-in, like the other SQL tests): the window is checked inside the order's transaction,
/// the limit holds when customers race, and the place is given back when an order is cancelled, rejected or its payment hold runs out.
/// Each test gives only its own store settings, so nothing else in the database is affected.
/// </summary>
[Collection("SqlServer")]
public sealed class SlotEfTests
{
    // Open all day, hourly windows, no cut-off, today and tomorrow: there is always a window to choose.
    private static SlotSettings Settings(int capacity = 3, bool asap = true, int cutoff = 0, bool enabled = true) => new(enabled, 0, 1440, 60, capacity, cutoff, 2, asap);

    private static async Task Configure(World world, SlotSettings settings)
    {
        await using var db = world.Db();
        await new EfSlotStore(db).SetAsync(world.Store.Id, settings, "test", DateTime.UtcNow);
    }

    private static Task<DateTime> NextWindow(World world, int skip = 0, int cutoff = 0) => Task.FromResult(SlotRules.Windows(Settings(cutoff: cutoff), DateTime.UtcNow, _ => 0)[skip].StartUtc);

    private static CheckoutCommit WithSlot(DateTime? start, string? key = null, string method = PaymentMethods.CashOnDelivery) =>
        Commit(key ?? "k" + Guid.NewGuid().ToString("N")[..12]) with { SlotStart = start, PaymentMethod = method };

    private static async Task<int> Booked(World world, DateTime start)
    {
        await using var db = world.Db();
        return await db.SlotBookings.AsNoTracking().Where(row => row.StoreId == world.Store.Id && row.SlotStart == start).Select(row => row.Booked).SingleOrDefaultAsync();
    }

    private static async Task<(Guid UserId, Guid OrganizationId)> UserOf(World world, Guid customerId)
    {
        await using var db = world.Db();
        return (await db.Customers.Where(customer => customer.Id == customerId).Select(customer => customer.UserId).SingleAsync(),
            await db.Stores.Where(store => store.Id == world.Store.Id).Select(store => store.OrganizationId).SingleAsync());
    }

    [Fact]
    public async Task A_chosen_window_is_kept_on_the_order_and_takes_a_place()
    {
        if (ConnectionString is null) { return; }
        await using var world = await World.CreateAsync(1, 10);
        await Configure(world, Settings());
        var start = await NextWindow(world);

        var result = await world.Checkout(world.CustomerIds[0], WithSlot(start));

        Assert.Equal(CheckoutCommitStatus.Succeeded, result.Status);
        Assert.Equal(start, result.Order!.DeliverySlotStart);
        Assert.Equal(start.AddMinutes(60), result.Order.DeliverySlotEnd);
        await using var db = world.Db();
        var order = await db.Orders.AsNoTracking().SingleAsync(item => item.Id == result.Order.Id);
        Assert.Equal(start, order.DeliverySlotStart);
        Assert.Equal(1, await Booked(world, start));
    }

    [Fact]
    public async Task An_order_without_a_choice_is_as_soon_as_possible_and_takes_no_place()
    {
        if (ConnectionString is null) { return; }
        await using var world = await World.CreateAsync(1, 10);
        await Configure(world, Settings());

        var result = await world.Checkout(world.CustomerIds[0], WithSlot(null));

        Assert.Equal(CheckoutCommitStatus.Succeeded, result.Status);
        Assert.Null(result.Order!.DeliverySlotStart);
        await using var db = world.Db();
        Assert.Empty(await db.SlotBookings.Where(row => row.StoreId == world.Store.Id).ToListAsync());
    }

    [Fact]
    public async Task A_store_that_does_not_schedule_refuses_a_chosen_time_and_changes_nothing()
    {
        if (ConnectionString is null) { return; }
        await using var world = await World.CreateAsync(1, 10);
        var start = DateTime.UtcNow.AddHours(3);

        var result = await world.Checkout(world.CustomerIds[0], WithSlot(start));

        Assert.Equal(CheckoutCommitStatus.SlotRejected, result.Status);
        Assert.Equal(10, await world.Stock());
        await using var db = world.Db();
        Assert.Empty(await db.Orders.Where(item => item.StoreId == world.Store.Id).ToListAsync());
    }

    [Fact]
    public async Task A_time_that_is_not_a_window_or_is_inside_the_cutoff_is_refused()
    {
        if (ConnectionString is null) { return; }
        await using var world = await World.CreateAsync(1, 10);
        await Configure(world, Settings(cutoff: 120));
        var good = await NextWindow(world, 0, 120);

        var off = await world.Checkout(world.CustomerIds[0], WithSlot(good.AddMinutes(30)));
        var soon = await world.Checkout(world.CustomerIds[0], WithSlot(good.AddHours(-3)));

        Assert.Equal(CheckoutCommitStatus.SlotRejected, off.Status);
        Assert.Equal(CheckoutCommitStatus.SlotRejected, soon.Status);
        Assert.Equal(10, await world.Stock());
    }

    [Fact]
    public async Task When_asap_is_switched_off_an_order_must_choose_a_window()
    {
        if (ConnectionString is null) { return; }
        await using var world = await World.CreateAsync(1, 10);
        await Configure(world, Settings(asap: false));

        var asap = await world.Checkout(world.CustomerIds[0], WithSlot(null));
        var chosen = await world.Checkout(world.CustomerIds[0], WithSlot(await NextWindow(world)));

        Assert.Equal(CheckoutCommitStatus.SlotRejected, asap.Status);
        Assert.Equal(CheckoutCommitStatus.Succeeded, chosen.Status);
    }

    [Fact]
    public async Task Customers_racing_for_the_last_place_cannot_both_get_it()
    {
        if (ConnectionString is null) { return; }
        await using var world = await World.CreateAsync(4, 10);
        await Configure(world, Settings(capacity: 1));
        var start = await NextWindow(world);

        var results = await Task.WhenAll(world.CustomerIds.Select(id => world.Checkout(id, WithSlot(start))));

        Assert.Equal(1, results.Count(r => r.Status == CheckoutCommitStatus.Succeeded));
        Assert.Equal(3, results.Count(r => r.Status == CheckoutCommitStatus.SlotRejected));
        Assert.Equal(1, await Booked(world, start));
        Assert.Equal(9, await world.Stock());
    }

    [Fact]
    public async Task A_full_window_is_refused_but_the_next_one_still_works()
    {
        if (ConnectionString is null) { return; }
        await using var world = await World.CreateAsync(3, 10);
        await Configure(world, Settings(capacity: 2));
        var first = await NextWindow(world);
        var second = await NextWindow(world, 1);

        Assert.Equal(CheckoutCommitStatus.Succeeded, (await world.Checkout(world.CustomerIds[0], WithSlot(first))).Status);
        Assert.Equal(CheckoutCommitStatus.Succeeded, (await world.Checkout(world.CustomerIds[1], WithSlot(first))).Status);
        var full = await world.Checkout(world.CustomerIds[2], WithSlot(first));
        var other = await world.Checkout(world.CustomerIds[2], WithSlot(second));

        Assert.Equal(CheckoutCommitStatus.SlotRejected, full.Status);
        Assert.Contains("full", full.Message);
        Assert.Equal(CheckoutCommitStatus.Succeeded, other.Status);
    }

    [Fact]
    public async Task Cancelling_gives_the_place_back_once_even_if_cancelled_twice()
    {
        if (ConnectionString is null) { return; }
        await using var world = await World.CreateAsync(2, 10);
        await Configure(world, Settings(capacity: 5));
        var start = await NextWindow(world);
        var first = await world.Checkout(world.CustomerIds[0], WithSlot(start));
        await world.Checkout(world.CustomerIds[1], WithSlot(start));
        Assert.Equal(2, await Booked(world, start));
        var (userId, organizationId) = await UserOf(world, world.CustomerIds[0]);

        for (var i = 0; i < 2; i++)
        {
            await using var db = world.Db();
            await new EfCommerceStore(db).TryCancelCustomerOrderAsync(first.Order!.Id, userId, organizationId);
        }

        Assert.Equal(1, await Booked(world, start)); // the other customer's place stays
    }

    [Fact]
    public async Task A_store_rejecting_the_order_gives_the_place_back()
    {
        if (ConnectionString is null) { return; }
        await using var world = await World.CreateAsync(1, 10);
        await Configure(world, Settings());
        var start = await NextWindow(world);
        var placed = await world.Checkout(world.CustomerIds[0], WithSlot(start));
        var (_, organizationId) = await UserOf(world, world.CustomerIds[0]);

        await using (var db = world.Db())
        {
            Assert.Equal(OrderLifecycleStatus.Succeeded, (await new EfCommerceStore(db).TryTransitionOrderAsync(placed.Order!.Id, organizationId, world.Store.Id, OrderStatus.Rejected)).Status);
        }

        Assert.Equal(0, await Booked(world, start));
    }

    [Fact]
    public async Task An_online_order_that_is_never_paid_gives_the_place_back_when_the_hold_runs_out()
    {
        if (ConnectionString is null) { return; }
        await using var world = await World.CreateAsync(1, 10);
        await Configure(world, Settings());
        var start = await NextWindow(world);
        var placed = await world.Checkout(world.CustomerIds[0], WithSlot(start, method: PaymentMethods.Online) with { PaymentExpiresAt = DateTime.UtcNow.AddMinutes(-1) });
        Assert.Equal(1, await Booked(world, start));

        await using (var db = world.Db())
        {
            Assert.Contains(placed.Order!.Id, await new EfPaymentStore(db).ReleaseExpiredAsync(DateTime.UtcNow, 50));
        }

        Assert.Equal(0, await Booked(world, start));
    }

    [Fact]
    public async Task The_store_sees_the_window_and_gets_orders_for_now_first_then_scheduled_ones_soonest_first()
    {
        if (ConnectionString is null) { return; }
        await using var world = await World.CreateAsync(3, 10);
        await Configure(world, Settings());
        var later = await NextWindow(world, 5);
        var sooner = await NextWindow(world, 2);
        var scheduledLate = await world.Checkout(world.CustomerIds[0], WithSlot(later));
        var scheduledSoon = await world.Checkout(world.CustomerIds[1], WithSlot(sooner));
        var now = await world.Checkout(world.CustomerIds[2], WithSlot(null));
        var (_, organizationId) = await UserOf(world, world.CustomerIds[0]);

        await using var db = world.Db();
        var list = (await new EfCommerceStore(db).GetScopedOrderSummariesAsync(organizationId, new HashSet<Guid> { world.Store.Id }, false))
            .Where(item => item.StoreId == world.Store.Id).ToList();

        Assert.Equal([now.Order!.Id, scheduledSoon.Order!.Id, scheduledLate.Order!.Id], list.Select(item => item.Id));
        Assert.Null(list[0].DeliverySlotStart);
        Assert.Equal(sooner, list[1].DeliverySlotStart);
        Assert.Equal(sooner.AddMinutes(60), list[1].DeliverySlotEnd);
    }

    [Fact]
    public async Task The_database_refuses_a_window_that_ends_before_it_starts_or_a_negative_count()
    {
        if (ConnectionString is null) { return; }
        await using var world = await World.CreateAsync(1, 10);
        var placed = await world.Checkout(world.CustomerIds[0], WithSlot(null));

        await using var db = world.Db();
        await Assert.ThrowsAnyAsync<Exception>(() => db.Orders.Where(item => item.Id == placed.Order!.Id).ExecuteUpdateAsync(set => set.SetProperty(item => item.DeliverySlotStart, DateTime.UtcNow).SetProperty(item => item.DeliverySlotEnd, DateTime.UtcNow.AddHours(-1))));
        await Assert.ThrowsAnyAsync<Exception>(() => db.Database.ExecuteSqlInterpolatedAsync($"INSERT INTO SlotBookings (StoreId, SlotStart, Booked) VALUES ({world.Store.Id}, {DateTime.UtcNow}, -1)"));
        await Assert.ThrowsAnyAsync<Exception>(() => db.Database.ExecuteSqlInterpolatedAsync($"INSERT INTO DeliverySlotSettings (Id, StoreId, Enabled, OpenMinutes, CloseMinutes, WindowMinutes, Capacity, CutoffMinutes, DaysAhead, AsapAllowed, UpdatedAt) VALUES ({Guid.NewGuid()}, {world.Store.Id}, 1, 600, 540, 60, 5, 0, 2, 1, {DateTime.UtcNow})"));
    }

    [Fact]
    public async Task The_service_saves_the_default_and_store_overrides_checks_them_and_lists_windows_for_a_customer()
    {
        if (ConnectionString is null) { return; }
        await using var world = await World.CreateAsync(1, 10);
        await using var db = world.Db();
        var organizationId = await db.Stores.Where(store => store.Id == world.Store.Id).Select(store => store.OrganizationId).SingleAsync();
        var (userId, _) = await UserOf(world, world.CustomerIds[0]);
        var store = new EfSlotStore(db);
        var original = await store.GetDefaultAsync();
        try
        {
            var admin = new SlotService(store, new EfCommerceStore(db), new FixedScope(true, organizationId), new FixedContext(null), TimeProvider.System);
            var customer = new SlotService(store, new EfCommerceStore(db), new FixedScope(false, organizationId), new FixedContext(new UserContext(userId, organizationId, null, Role.Customer)));

            Assert.Equal(SlotServiceStatus.Forbidden, (await customer.GetRulesAsync()).Status);
            Assert.Equal(SlotServiceStatus.Invalid, (await admin.SetDefaultAsync(new SlotSettingsDto(true, "9am", "21:00", 60, 5, 60, 2, true))).Status);
            Assert.Equal(SlotServiceStatus.Invalid, (await admin.SetDefaultAsync(new SlotSettingsDto(true, "09:00", "21:00", 50, 5, 60, 2, true))).Status);
            Assert.Equal(SlotServiceStatus.NotFound, (await admin.SetStoreAsync(Guid.NewGuid(), new SlotSettingsDto(true, "09:00", "21:00", 60, 5, 60, 2, true))).Status);

            var saved = await admin.SetDefaultAsync(new SlotSettingsDto(false, "09:00", "21:00", 60, 5, 60, 2, true));
            Assert.Equal(SlotServiceStatus.Succeeded, saved.Status);
            Assert.False(saved.Value!.Default.Enabled);

            // With the default off a customer sees no windows; the store's own settings switch it on for that store only.
            Assert.False((await customer.ListAsync(world.Store.Id)).Value!.Enabled);
            var own = await admin.SetStoreAsync(world.Store.Id, new SlotSettingsDto(true, "00:00", "24:00", 60, 2, 0, 2, false));
            Assert.Equal(SlotServiceStatus.Succeeded, own.Status);
            Assert.NotNull(own.Value!.Stores.Single(item => item.StoreId == world.Store.Id).Override);

            var start = await NextWindow(world);
            await world.Checkout(world.CustomerIds[0], WithSlot(start));
            var listed = (await customer.ListAsync(world.Store.Id)).Value!;
            Assert.True(listed.Enabled);
            Assert.False(listed.AsapAllowed);
            var window = listed.Windows.Single(item => item.StartUtc == start);
            Assert.Equal(1, window.Left); // capacity 2, one taken
            Assert.InRange(listed.Windows.Count, 25, 48); // today's remaining hourly windows plus all 24 of tomorrow
            Assert.All(listed.Windows, item => Assert.False(string.IsNullOrEmpty(item.TimeLabel)));

            var cleared = await admin.SetStoreAsync(world.Store.Id, null);
            Assert.Null(cleared.Value!.Stores.Single(item => item.StoreId == world.Store.Id).Override);
            Assert.False((await customer.ListAsync(world.Store.Id)).Value!.Enabled);

            Assert.Equal(SlotServiceStatus.Forbidden, (await customer.SetDefaultAsync(new SlotSettingsDto(true, "09:00", "21:00", 60, 5, 60, 2, true))).Status);
        }
        finally
        {
            await store.SetAsync(null, original is null ? null : SlotSettings.From(original), "test", DateTime.UtcNow);
        }
    }

    private sealed class FixedScope(bool admin, Guid organizationId) : IAuthorizationScopeService
    {
        private AuthorizationScope Scope => new(Guid.NewGuid(), organizationId, new HashSet<string> { admin ? "ApplicationAdmin" : "StoreStaff" }, new HashSet<Guid>(), new HashSet<string>());
        public Task<AuthorizationScope?> ResolveAsync(CancellationToken cancellationToken = default) => Task.FromResult<AuthorizationScope?>(Scope);
        public Task<AuthorizationScope?> ResolveForUserAsync(Guid userId, CancellationToken cancellationToken = default) => Task.FromResult<AuthorizationScope?>(Scope);
        public Task<bool> HasPermissionAsync(string permissionCode, CancellationToken cancellationToken = default) => Task.FromResult(admin);
        public Task<bool> CanAccessStoreAsync(Guid storeId, CancellationToken cancellationToken = default) => Task.FromResult(admin);
        public Task<bool> OwnsCustomerAsync(Guid customerId, CancellationToken cancellationToken = default) => Task.FromResult(false);
    }

    private sealed class FixedContext(UserContext? context) : ICurrentUserContextResolver
    {
        public Task<UserContext?> ResolveAsync(CancellationToken cancellationToken = default) => Task.FromResult(context);
    }
}
