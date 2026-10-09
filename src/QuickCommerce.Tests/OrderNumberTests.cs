using Microsoft.EntityFrameworkCore;
using QuickCommerce.Application.DTOs;
using QuickCommerce.Domain;
using QuickCommerce.Infrastructure.Persistence;
using Xunit;

namespace QuickCommerce.Tests;

/// <summary>Task 10.x: order numbers look like KHG-261009-0042 (store code, India day, that store's count for the day).</summary>
public sealed class OrderNumberTests
{
    [Fact]
    public void A_number_is_the_store_code_the_day_and_a_four_digit_count()
    {
        Assert.Equal("KHG-261009-0042", OrderNumbers.Format("KHG", new DateOnly(2026, 10, 9), 42));
        Assert.Equal("KHG-261009-0001", OrderNumbers.Format("KHG", new DateOnly(2026, 10, 9), 1));
        Assert.Equal("KHG-261009-12345", OrderNumbers.Format("KHG", new DateOnly(2026, 10, 9), 12345));
    }

    [Fact]
    public void The_day_is_the_indian_calendar_day_not_the_utc_day()
    {
        Assert.Equal(new DateOnly(2026, 10, 9), OrderNumbers.DayOf(new DateTime(2026, 10, 9, 18, 29, 0, DateTimeKind.Utc)));
        Assert.Equal(new DateOnly(2026, 10, 10), OrderNumbers.DayOf(new DateTime(2026, 10, 9, 18, 30, 0, DateTimeKind.Utc)));
        Assert.Equal(new DateOnly(2026, 10, 10), OrderNumbers.DayOf(new DateTime(2026, 10, 9, 23, 59, 0, DateTimeKind.Utc)));
        Assert.Equal(new DateOnly(2026, 10, 10), OrderNumbers.DayOf(new DateTime(2026, 10, 10, 0, 0, 0, DateTimeKind.Utc)));
    }

    [Fact]
    public void A_store_without_a_code_still_gets_a_usable_prefix_and_codes_are_upper_case()
    {
        var store = new Store { Name = "x", Address = "y" };
        var stand = OrderNumbers.CodeOf(store);

        Assert.Equal(4, stand.Length);
        Assert.Equal(stand, OrderNumbers.CodeOf(store));
        Assert.Equal("KHG", OrderNumbers.CodeOf(new Store { Name = "x", Address = "y", Code = " khg " }));
    }

    [Fact]
    public async Task Orders_in_a_store_are_numbered_one_after_another_and_each_store_counts_on_its_own()
    {
        var rig = PaymentRig.Create();
        var other = rig.Data.Stores.First(store => store.Id != rig.Store.Id);

        var first = await rig.PlaceOnlineAsync(method: PaymentMethods.CashOnDelivery, key: "num-key-0000001");
        var second = await rig.PlaceOnlineAsync(method: PaymentMethods.CashOnDelivery, key: "num-key-0000002");
        // An order in another store, placed straight through the store.
        var elsewhere = new Order { OrderNumber = "x", UserId = rig.Data.Users.Single().Id, StoreId = other.Id, DeliveryAddress = "a", TotalAmount = 10, Items = [] };
        Assert.True(await rig.Data.TryCreateOrderAsync(elsewhere, []));

        var day = OrderNumbers.DayOf(DateTime.UtcNow);
        Assert.Equal(OrderNumbers.Format(OrderNumbers.CodeOf(rig.Store), day, 1), first.OrderNumber);
        Assert.Equal(OrderNumbers.Format(OrderNumbers.CodeOf(rig.Store), day, 2), second.OrderNumber);
        Assert.Equal(OrderNumbers.Format(OrderNumbers.CodeOf(other), day, 1), elsewhere.OrderNumber);
        Assert.StartsWith("HBR-", first.OrderNumber);
    }

    [Fact]
    public async Task A_refused_order_does_not_use_up_a_number()
    {
        var rig = PaymentRig.Create();
        await rig.Cart.AddItemAsync(rig.Store.Id, new AddCartItemRequest(rig.Tomato.Id, 1));
        rig.Data.StockOf(rig.Store, rig.Tomato).AvailableQuantity = 0;
        var refused = await rig.Checkout.CheckoutAsync(rig.Store.Id, rig.Where(PaymentMethods.CashOnDelivery), "num-refused-00001");
        Assert.NotEqual(CheckoutOperationStatus.Succeeded, refused.Status);
        rig.Data.StockOf(rig.Store, rig.Tomato).AvailableQuantity = 50;

        var placed = await rig.Checkout.CheckoutAsync(rig.Store.Id, rig.Where(PaymentMethods.CashOnDelivery), "num-refused-00002");

        Assert.EndsWith("-0001", placed.Order!.OrderNumber);
    }
}

/// <summary>The same rules on SQL Server: no repeats under parallel orders, a new day starts again at 1, and the store code index.</summary>
public sealed class OrderNumberEfIntegrationTests
{
    private static string? ConnectionString => CheckoutEfIntegrationTests.ConnectionString;

    private static async Task<string> GiveCodeAsync(CheckoutEfIntegrationTests.World world)
    {
        var code = "T" + Guid.NewGuid().ToString("N")[..5].ToUpperInvariant();
        await using var db = world.Db();
        await db.Stores.Where(store => store.Id == world.Store.Id).ExecuteUpdateAsync(set => set.SetProperty(store => store.Code, code));
        return code;
    }

    [Fact]
    public async Task Numbers_follow_the_store_code_and_count_up_with_no_gaps()
    {
        if (ConnectionString is null)
        {
            return;
        }

        await using var world = await CheckoutEfIntegrationTests.World.CreateAsync(customers: 3, stock: 20, cartQuantity: 1);
        var code = await GiveCodeAsync(world);
        var day = OrderNumbers.DayOf(DateTime.UtcNow);

        var numbers = new List<string>();
        foreach (var customer in world.CustomerIds)
        {
            numbers.Add((await world.Checkout(customer, CheckoutEfIntegrationTests.Commit(null))).Order!.OrderNumber);
        }

        Assert.Equal(Enumerable.Range(1, 3).Select(n => OrderNumbers.Format(code, day, n)), numbers);
    }

    [Fact]
    public async Task Orders_placed_at_the_same_moment_never_share_a_number()
    {
        if (ConnectionString is null)
        {
            return;
        }

        await using var world = await CheckoutEfIntegrationTests.World.CreateAsync(customers: 4, stock: 30, cartQuantity: 1);
        var code = await GiveCodeAsync(world);
        var day = OrderNumbers.DayOf(DateTime.UtcNow);

        var results = await Task.WhenAll(world.CustomerIds.Select(customer => world.Checkout(customer, CheckoutEfIntegrationTests.Commit(null))));

        var numbers = results.Select(result => result.Order!.OrderNumber).Order().ToArray();
        Assert.All(results, result => Assert.Equal(CheckoutCommitStatus.Succeeded, result.Status));
        Assert.Equal(Enumerable.Range(1, 4).Select(n => OrderNumbers.Format(code, day, n)), numbers);
    }

    [Fact]
    public async Task A_new_day_starts_again_at_one_and_yesterdays_count_is_kept()
    {
        if (ConnectionString is null)
        {
            return;
        }

        await using var world = await CheckoutEfIntegrationTests.World.CreateAsync(customers: 1, stock: 20, cartQuantity: 1);
        var code = await GiveCodeAsync(world);
        var today = OrderNumbers.DayOf(DateTime.UtcNow);
        await using (var db = world.Db())
        {
            db.OrderNumberCounters.Add(new OrderNumberCounter { StoreId = world.Store.Id, Day = today.AddDays(-1), LastNumber = 99 });
            await db.SaveChangesAsync();
        }

        var placed = await world.Checkout(world.CustomerIds.Single(), CheckoutEfIntegrationTests.Commit(null));

        Assert.Equal(OrderNumbers.Format(code, today, 1), placed.Order!.OrderNumber);
        await using var verify = world.Db();
        Assert.Equal(99, (await verify.OrderNumberCounters.AsNoTracking().SingleAsync(item => item.StoreId == world.Store.Id && item.Day == today.AddDays(-1))).LastNumber);
    }

    [Fact]
    public async Task An_order_that_is_refused_gives_its_number_back()
    {
        if (ConnectionString is null)
        {
            return;
        }

        await using var world = await CheckoutEfIntegrationTests.World.CreateAsync(customers: 2, stock: 1, cartQuantity: 2);
        var code = await GiveCodeAsync(world);
        var day = OrderNumbers.DayOf(DateTime.UtcNow);

        var refused = await world.Checkout(world.CustomerIds[0], CheckoutEfIntegrationTests.Commit(null));
        await using (var db = world.Db())
        {
            await db.StoreVariantInventory.Where(row => row.StoreId == world.Store.Id).ExecuteUpdateAsync(set => set.SetProperty(row => row.AvailableQuantity, 10));
        }

        var placed = await world.Checkout(world.CustomerIds[1], CheckoutEfIntegrationTests.Commit(null));

        Assert.NotEqual(CheckoutCommitStatus.Succeeded, refused.Status);
        Assert.Equal(OrderNumbers.Format(code, day, 1), placed.Order!.OrderNumber);
    }

    [Fact]
    public async Task Two_stores_cannot_share_a_code()
    {
        if (ConnectionString is null)
        {
            return;
        }

        await using var first = await CheckoutEfIntegrationTests.World.CreateAsync(customers: 1, stock: 5);
        await using var second = await CheckoutEfIntegrationTests.World.CreateAsync(customers: 1, stock: 5);
        var code = await GiveCodeAsync(first);

        await using var db = second.Db();
        await Assert.ThrowsAnyAsync<Exception>(() => db.Stores.Where(store => store.Id == second.Store.Id).ExecuteUpdateAsync(set => set.SetProperty(store => store.Code, code)));
    }
}
