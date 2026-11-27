using Microsoft.EntityFrameworkCore;
using QuickCommerce.Application.Services;
using QuickCommerce.Domain;
using QuickCommerce.Infrastructure;
using QuickCommerce.Infrastructure.Persistence;
using Xunit;

namespace QuickCommerce.Tests;

/// <summary>The store's order list shows what needs the staff first: new orders, then those being handled, and finished ones last. Newest first within each.</summary>
public sealed class OrderListOrderTests
{
    private static readonly DateTime Base = new(2026, 10, 14, 6, 0, 0, DateTimeKind.Utc);

    // (number, status, minutes after Base): deliberately not in the order they should come out.
    private static readonly (string Number, OrderStatus Status, int Minutes)[] Mixed =
    [
        ("DONE-NEW", OrderStatus.Delivered, 90),
        ("PEND-OLD", OrderStatus.Pending, 10),
        ("READY", OrderStatus.Ready, 50),
        ("CANCEL", OrderStatus.Cancelled, 100),
        ("PEND-NEW", OrderStatus.Pending, 80),
        ("PACK", OrderStatus.Preparing, 60),
        ("OUT", OrderStatus.OutForDelivery, 70),
        ("ACCEPT", OrderStatus.Accepted, 40),
        ("REJECT", OrderStatus.Rejected, 20),
        ("DONE-OLD", OrderStatus.Completed, 5),
        ("CONFIRMED", OrderStatus.Confirmed, 30)
    ];

    private static readonly string[] Expected = ["PEND-NEW", "PEND-OLD", "ACCEPT", "PACK", "READY", "OUT", "CONFIRMED", "CANCEL", "DONE-NEW", "REJECT", "DONE-OLD"];

    [Fact]
    public void New_orders_rank_before_orders_in_progress_and_finished_ones_rank_last()
    {
        Assert.Equal(0, OrderAttention.Rank(OrderStatus.Pending));
        Assert.True(OrderAttention.Rank(OrderStatus.Pending) < OrderAttention.Rank(OrderStatus.Accepted));
        Assert.True(OrderAttention.Rank(OrderStatus.Accepted) < OrderAttention.Rank(OrderStatus.Preparing));
        Assert.True(OrderAttention.Rank(OrderStatus.Preparing) < OrderAttention.Rank(OrderStatus.Ready));
        Assert.True(OrderAttention.Rank(OrderStatus.Ready) < OrderAttention.Rank(OrderStatus.OutForDelivery));
        foreach (var finished in new[] { OrderStatus.Delivered, OrderStatus.Completed, OrderStatus.Cancelled, OrderStatus.Rejected })
        {
            Assert.True(OrderAttention.Rank(OrderStatus.OutForDelivery) < OrderAttention.Rank(finished));
        }
    }

    [Fact]
    public async Task The_in_memory_list_puts_the_newest_new_order_first_and_finished_orders_last()
    {
        var data = InMemoryCommerceStore.CreateSeeded();
        var store = data.Stores[0];
        var user = data.Users.First();
        foreach (var (number, status, minutes) in Mixed)
        {
            data.Orders.Add(new Order { OrderNumber = number, UserId = user.Id, StoreId = store.Id, Status = status, DeliveryAddress = "x", TotalAmount = 10, CreatedAt = Base.AddMinutes(minutes) });
        }

        var list = await data.GetScopedOrderSummariesAsync(data.Organizations.Single().Id, new HashSet<Guid> { store.Id }, false);

        Assert.Equal(Expected, list.Where(order => Mixed.Any(item => item.Number == order.OrderNumber)).Select(order => order.OrderNumber));
    }

    [Fact]
    public async Task An_unpaid_hold_is_not_in_the_list_at_all()
    {
        var data = InMemoryCommerceStore.CreateSeeded();
        var store = data.Stores[0];
        data.Orders.Add(new Order { OrderNumber = "HOLD", UserId = data.Users.First().Id, StoreId = store.Id, Status = OrderStatus.AwaitingPayment, DeliveryAddress = "x", TotalAmount = 10 });

        var list = await data.GetScopedOrderSummariesAsync(data.Organizations.Single().Id, new HashSet<Guid> { store.Id }, false);

        Assert.DoesNotContain(list, order => order.OrderNumber == "HOLD");
    }
}

[Collection("SqlServer")]
public sealed class OrderListOrderEfIntegrationTests
{
    [Fact]
    public async Task The_sql_list_comes_out_in_the_same_order_as_the_in_memory_one()
    {
        if (CheckoutEfIntegrationTests.ConnectionString is null)
        {
            return;
        }

        await using var world = await CheckoutEfIntegrationTests.World.CreateAsync(customers: 1, stock: 5);
        var @base = new DateTime(2026, 10, 14, 6, 0, 0, DateTimeKind.Utc);
        var mixed = new (string Number, OrderStatus Status, int Minutes)[]
        {
            ("DONE-NEW", OrderStatus.Delivered, 90), ("PEND-OLD", OrderStatus.Pending, 10), ("READY", OrderStatus.Ready, 50), ("CANCEL", OrderStatus.Cancelled, 100),
            ("PEND-NEW", OrderStatus.Pending, 80), ("PACK", OrderStatus.Preparing, 60), ("OUT", OrderStatus.OutForDelivery, 70), ("ACCEPT", OrderStatus.Accepted, 40),
            ("REJECT", OrderStatus.Rejected, 20), ("DONE-OLD", OrderStatus.Completed, 5), ("CONFIRMED", OrderStatus.Confirmed, 30)
        };
        string[] expected = ["PEND-NEW", "PEND-OLD", "ACCEPT", "PACK", "READY", "OUT", "CONFIRMED", "CANCEL", "DONE-NEW", "REJECT", "DONE-OLD"];
        Guid organization;
        await using (var db = world.Db())
        {
            var user = await db.Users.Where(item => item.ExternalSubject == world.Emails[0]).Select(item => item.Id).SingleAsync();
            organization = world.Store.OrganizationId;
            foreach (var (number, status, minutes) in mixed)
            {
                db.Orders.Add(new Order { OrderNumber = $"{number}|{world.Token}", UserId = user, StoreId = world.Store.Id, Status = status, DeliveryAddress = "x", TotalAmount = 10, CreatedAt = @base.AddMinutes(minutes) });
            }

            await db.SaveChangesAsync();
        }

        await using var read = world.Db();
        var list = await new EfCommerceStore(read).GetScopedOrderSummariesAsync(organization, new HashSet<Guid> { world.Store.Id }, false);

        Assert.Equal(expected, list.Select(order => order.OrderNumber.Split('|')[0]));
    }
}
