using Microsoft.EntityFrameworkCore;
using QuickCommerce.Application.DTOs;
using QuickCommerce.Domain;
using QuickCommerce.Infrastructure.Persistence;
using Xunit;

namespace QuickCommerce.Tests;

/// <summary>
/// Phase P6 against a real SQL Server: cancelling an order puts the stock back exactly once, and cancelling and the shop accepting
/// at the same moment ends with exactly one of them having won. Opt-in, like the other SQL tests: set QUICKCOMMERCE_TEST_CONNECTION_STRING
/// to a connection string for a DISPOSABLE database. Everything it adds is uniquely named and removed again.
/// </summary>
public sealed class OrderCancelEfIntegrationTests
{
    private static string? ConnectionString => CheckoutEfIntegrationTests.ConnectionString;

    private static async Task<(Guid UserId, Guid OrganizationId)> OwnerAsync(CheckoutEfIntegrationTests.World world, Guid customerId)
    {
        await using var db = world.Db();
        var userId = await db.Customers.Where(customer => customer.Id == customerId).Select(customer => customer.UserId).SingleAsync();
        var organizationId = await db.Stores.Where(store => store.Id == world.Store.Id).Select(store => store.OrganizationId).SingleAsync();
        return (userId, organizationId);
    }

    private static async Task<CancelCommitResult> CancelAsync(CheckoutEfIntegrationTests.World world, Guid orderId, Guid userId, Guid organizationId)
    {
        await using var db = world.Db();
        return await new EfCommerceStore(db).TryCancelCustomerOrderAsync(orderId, userId, organizationId);
    }

    [Fact]
    public async Task Cancelling_puts_the_stock_back_writes_a_notification_and_a_second_cancel_changes_nothing()
    {
        if (ConnectionString is null)
        {
            return;
        }

        await using var world = await CheckoutEfIntegrationTests.World.CreateAsync(customers: 1, stock: 10, cartQuantity: 3);
        var customer = world.CustomerIds.Single();
        var (userId, organizationId) = await OwnerAsync(world, customer);
        var placed = await world.Checkout(customer, CheckoutEfIntegrationTests.Commit(null));
        Assert.Equal(7, await world.Stock());

        var first = await CancelAsync(world, placed.Order!.Id, userId, organizationId);
        var second = await CancelAsync(world, placed.Order.Id, userId, organizationId);

        Assert.Equal(CancelCommitStatus.Cancelled, first.Status);
        Assert.Equal(CancelCommitStatus.AlreadyCancelled, second.Status);
        Assert.Equal(10, await world.Stock());
        await using var verify = world.Db();
        var order = await verify.Orders.AsNoTracking().Include(o => o.StatusHistory).SingleAsync(o => o.Id == placed.Order.Id);
        Assert.Equal(OrderStatus.Cancelled, order.Status);
        Assert.Equal(2, order.StatusHistory.Count);
        var notes = await verify.Notifications.Where(n => n.OrderId == order.Id).ToListAsync();
        var note = Assert.Single(notes);
        Assert.Equal(("Order cancelled", "Your order was cancelled.", false), (note.Title, note.Message, note.IsRead));
    }

    [Fact]
    public async Task Cancelling_from_several_places_at_once_returns_the_stock_once()
    {
        if (ConnectionString is null)
        {
            return;
        }

        await using var world = await CheckoutEfIntegrationTests.World.CreateAsync(customers: 1, stock: 10, cartQuantity: 4);
        var customer = world.CustomerIds.Single();
        var (userId, organizationId) = await OwnerAsync(world, customer);
        var placed = await world.Checkout(customer, CheckoutEfIntegrationTests.Commit(null));

        var results = await Task.WhenAll(Enumerable.Range(0, 6).Select(_ => CancelAsync(world, placed.Order!.Id, userId, organizationId)));

        Assert.All(results, result => Assert.True(result.Status is CancelCommitStatus.Cancelled or CancelCommitStatus.AlreadyCancelled, result.Status.ToString()));
        Assert.Equal(1, results.Count(result => result.Status == CancelCommitStatus.Cancelled));
        Assert.Equal(10, await world.Stock());
        await using var verify = world.Db();
        Assert.Equal(1, await verify.Notifications.CountAsync(n => n.OrderId == placed.Order!.Id));
        Assert.Equal(2, await verify.OrderStatusHistory.CountAsync(h => EF.Property<Guid>(h, "OrderId") == placed.Order!.Id));
    }

    [Fact]
    public async Task The_customer_cancelling_and_the_shop_accepting_at_the_same_moment_end_with_one_winner_and_the_stock_right()
    {
        if (ConnectionString is null)
        {
            return;
        }

        for (var round = 0; round < 6; round++)
        {
            await using var world = await CheckoutEfIntegrationTests.World.CreateAsync(customers: 1, stock: 10, cartQuantity: 2);
            var customer = world.CustomerIds.Single();
            var (userId, organizationId) = await OwnerAsync(world, customer);
            var placed = await world.Checkout(customer, CheckoutEfIntegrationTests.Commit(null));

            var cancel = CancelAsync(world, placed.Order!.Id, userId, organizationId);
            var accept = Task.Run(async () =>
            {
                await using var db = world.Db();
                return await new EfCommerceStore(db).TryTransitionOrderAsync(placed.Order.Id, organizationId, null, OrderStatus.Accepted);
            });
            await Task.WhenAll(cancel, accept);

            await using var verify = world.Db();
            var final = await verify.Orders.AsNoTracking().SingleAsync(o => o.Id == placed.Order.Id);
            var cancelled = cancel.Result.Status is CancelCommitStatus.Cancelled or CancelCommitStatus.AlreadyCancelled;
            var accepted = accept.Result.Status == OrderLifecycleStatus.Succeeded;
            Assert.NotEqual(cancelled, accepted); // exactly one of the two
            Assert.Equal(cancelled ? OrderStatus.Cancelled : OrderStatus.Accepted, final.Status);
            Assert.Equal(cancelled ? 10 : 8, await world.Stock());
            Assert.Equal(1, await verify.Notifications.CountAsync(n => n.OrderId == final.Id));
        }
    }

    [Fact]
    public async Task An_accepted_order_is_refused_and_another_customers_order_is_not_found()
    {
        if (ConnectionString is null)
        {
            return;
        }

        await using var world = await CheckoutEfIntegrationTests.World.CreateAsync(customers: 2, stock: 10, cartQuantity: 1);
        var (firstUser, organizationId) = await OwnerAsync(world, world.CustomerIds[0]);
        var (secondUser, _) = await OwnerAsync(world, world.CustomerIds[1]);
        var placed = await world.Checkout(world.CustomerIds[0], CheckoutEfIntegrationTests.Commit(null));

        var stranger = await CancelAsync(world, placed.Order!.Id, secondUser, organizationId);
        await using (var db = world.Db())
        {
            await new EfCommerceStore(db).TryTransitionOrderAsync(placed.Order.Id, organizationId, null, OrderStatus.Accepted);
        }

        var refused = await CancelAsync(world, placed.Order.Id, firstUser, organizationId);

        Assert.Equal(CancelCommitStatus.NotFound, stranger.Status);
        Assert.Equal((CancelCommitStatus.NotCancellable, OrderStatus.Accepted), (refused.Status, refused.CurrentStatus));
        Assert.Equal(9, await world.Stock());
    }

    [Fact]
    public async Task The_estimate_and_the_store_phone_are_stored_and_the_unread_count_and_mark_all_work()
    {
        if (ConnectionString is null)
        {
            return;
        }

        await using var world = await CheckoutEfIntegrationTests.World.CreateAsync(customers: 2, stock: 10, cartQuantity: 1);
        var customer = world.CustomerIds[0];
        var other = world.CustomerIds[1];
        await using (var db = world.Db())
        {
            await db.Stores.Where(s => s.Id == world.Store.Id).ExecuteUpdateAsync(set => set.SetProperty(s => s.PhoneNumber, "02212345678"));
            db.Notifications.AddRange(
                new Notification { CustomerId = customer, Type = "t", Title = "a", Message = "m" },
                new Notification { CustomerId = customer, Type = "t", Title = "b", Message = "m" },
                new Notification { CustomerId = customer, Type = "t", Title = "c", Message = "m", IsRead = true },
                new Notification { CustomerId = other, Type = "t", Title = "d", Message = "m" });
            await db.SaveChangesAsync();
        }

        var commit = CheckoutEfIntegrationTests.Commit(null) with { EstimatedDeliveryMinutes = 17 };
        var placed = await world.Checkout(customer, commit);

        await using var verify = world.Db();
        var store = new EfCommerceStore(verify);
        Assert.Equal(17, placed.Order!.EstimatedDeliveryMinutes);
        Assert.Equal(17, (await verify.Orders.AsNoTracking().SingleAsync(o => o.Id == placed.Order.Id)).EstimatedDeliveryMinutes);
        Assert.Equal("02212345678", (await store.GetStoreAsync(world.Store.Id))!.PhoneNumber);
        Assert.Equal(2, await store.GetUnreadNotificationCountAsync(customer));
        Assert.Equal(2, await store.MarkAllNotificationsReadAsync(customer));
        Assert.Equal(0, await store.MarkAllNotificationsReadAsync(customer));
        Assert.Equal(0, await store.GetUnreadNotificationCountAsync(customer));
        Assert.Equal(1, await store.GetUnreadNotificationCountAsync(other));
        await verify.Notifications.Where(n => n.CustomerId == customer || n.CustomerId == other).ExecuteDeleteAsync();
    }
}
