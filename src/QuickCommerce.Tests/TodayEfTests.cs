using Microsoft.EntityFrameworkCore;
using QuickCommerce.Application.Interfaces;
using QuickCommerce.Application.Services;
using QuickCommerce.Domain;
using QuickCommerce.Infrastructure.Persistence;
using Xunit;
using static QuickCommerce.Tests.CheckoutEfIntegrationTests;

namespace QuickCommerce.Tests;

/// <summary>The data behind the Today screen on a real SQL Server (opt-in, like the other SQL tests): which orders are read, when they became new or ready, and the low stock.</summary>
[Collection("SqlServer")]
public sealed class TodayEfTests
{
    private static async Task SetStatus(World world, Guid orderId, OrderStatus status, DateTime? at = null)
    {
        await using var db = world.Db();
        var order = await db.Orders.Include(item => item.StatusHistory).SingleAsync(item => item.Id == orderId);
        order.Status = status;
        order.StatusHistory.Add(new OrderStatusHistory { Status = status, ChangedAt = at ?? DateTime.UtcNow });
        await db.SaveChangesAsync();
    }

    [Fact]
    public async Task Open_orders_are_read_with_when_they_became_new_and_ready_and_an_unpaid_online_order_is_left_out()
    {
        if (ConnectionString is null) { return; }
        await using var world = await World.CreateAsync(4, 20);
        var pending = await world.Checkout(world.CustomerIds[0], Commit("k" + Guid.NewGuid().ToString("N")[..12]));
        var ready = await world.Checkout(world.CustomerIds[1], Commit("k" + Guid.NewGuid().ToString("N")[..12]));
        var done = await world.Checkout(world.CustomerIds[2], Commit("k" + Guid.NewGuid().ToString("N")[..12]));
        var unpaid = await world.Checkout(world.CustomerIds[3], Commit("k" + Guid.NewGuid().ToString("N")[..12]) with { PaymentMethod = PaymentMethods.Online, PaymentExpiresAt = DateTime.UtcNow.AddMinutes(15) });
        var readyAt = DateTime.UtcNow.AddMinutes(-25);
        await SetStatus(world, ready.Order!.Id, OrderStatus.Ready, readyAt);
        await SetStatus(world, done.Order!.Id, OrderStatus.Delivered);

        await using var db = world.Db();
        var rows = await new EfTodayStore(db).GetOrderRowsAsync(world.Store.Id, DateTime.UtcNow.AddHours(-1));

        Assert.Equal(3, rows.Count);
        Assert.DoesNotContain(rows, row => row.Id == unpaid.Order!.Id);
        var waiting = rows.Single(row => row.Id == pending.Order!.Id);
        Assert.Equal(OrderStatus.Pending, waiting.Status);
        Assert.Null(waiting.ReadyAt);
        Assert.True(waiting.PendingAt <= DateTime.UtcNow && waiting.PendingAt > DateTime.UtcNow.AddMinutes(-5));
        Assert.Equal(readyAt, rows.Single(row => row.Id == ready.Order!.Id).ReadyAt!.Value, TimeSpan.FromSeconds(1));
        Assert.False(rows.Single(row => row.Id == ready.Order!.Id).HasPartner);
    }

    [Fact]
    public async Task An_order_placed_before_today_is_still_read_while_open_but_a_finished_old_one_is_not()
    {
        if (ConnectionString is null) { return; }
        await using var world = await World.CreateAsync(2, 20);
        var open = await world.Checkout(world.CustomerIds[0], Commit("k" + Guid.NewGuid().ToString("N")[..12]));
        var finished = await world.Checkout(world.CustomerIds[1], Commit("k" + Guid.NewGuid().ToString("N")[..12]));
        await SetStatus(world, finished.Order!.Id, OrderStatus.Delivered);
        await using (var db = world.Db())
        {
            await db.Orders.Where(item => item.StoreId == world.Store.Id).ExecuteUpdateAsync(set => set.SetProperty(item => item.CreatedAt, DateTime.UtcNow.AddDays(-3)));
        }

        await using var read = world.Db();
        var rows = await new EfTodayStore(read).GetOrderRowsAsync(world.Store.Id, DateTime.UtcNow.AddHours(-2));

        Assert.Equal([open.Order!.Id], rows.Select(row => row.Id));
    }

    [Fact]
    public async Task Low_and_empty_stock_of_the_store_is_read_fewest_first_and_other_stores_are_not()
    {
        if (ConnectionString is null) { return; }
        await using var world = await World.CreateAsync(1, 3);
        await using var db = world.Db();

        var low = await new EfTodayStore(db).GetLowStockAsync(world.Store.Id, 10);
        Assert.Equal([3], low.Select(row => row.Available)); // 3 left, reorder level 5

        await db.StoreVariantInventory.Where(row => row.StoreId == world.Store.Id).ExecuteUpdateAsync(set => set.SetProperty(row => row.AvailableQuantity, 0));
        var none = await new EfTodayStore(db).GetLowStockAsync(world.Store.Id, 10);
        Assert.Equal(0, Assert.Single(none).Available);
        Assert.Empty(await new EfTodayStore(db).GetLowStockAsync(Guid.NewGuid(), 10));
    }

    [Fact]
    public async Task The_store_is_found_only_in_its_own_organisation()
    {
        if (ConnectionString is null) { return; }
        await using var world = await World.CreateAsync(1, 10);
        await using var db = world.Db();

        Assert.NotNull(await new EfTodayStore(db).GetStoreAsync(world.Store.OrganizationId, world.Store.Id));
        Assert.Null(await new EfTodayStore(db).GetStoreAsync(Guid.NewGuid(), world.Store.Id));
    }
}
