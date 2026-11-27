using Microsoft.EntityFrameworkCore;
using QuickCommerce.Application.DTOs;
using QuickCommerce.Application.Interfaces;
using QuickCommerce.Application.Services;
using QuickCommerce.Domain;
using QuickCommerce.Infrastructure.Persistence;
using Xunit;
using static QuickCommerce.Tests.CheckoutEfIntegrationTests;

namespace QuickCommerce.Tests;

public sealed class OrderIssueRuleTests
{
    private static Order Order(decimal coupon = 0)
    {
        var product = Guid.NewGuid();
        return new Order { OrderNumber = "O", DeliveryAddress = "x", SubtotalAmount = 100, CouponDiscount = coupon, Items = [new OrderItem { ProductId = product, ProductNameSnapshot = "Rice", UnitPrice = 50, Quantity = 2 }] };
    }

    [Fact]
    public void A_claim_is_what_was_paid_for_those_items()
    {
        var order = Order(coupon: 10);
        var (items, problem) = OrderIssueRules.Claim(order, [new IssueLineRequest(order.Items[0].ProductId, null, 1)]);

        Assert.Null(problem);
        Assert.Equal(45m, Assert.Single(items!).Amount);
        Assert.Null(OrderIssueRules.Claim(order, [new IssueLineRequest(order.Items[0].ProductId, null, 3)]).Items);
        Assert.Null(OrderIssueRules.Claim(order, [new IssueLineRequest(Guid.NewGuid(), null, 1)]).Items);
        Assert.Null(OrderIssueRules.Claim(order, []).Items);
    }

    [Fact]
    public void A_problem_can_be_reported_for_48_hours_after_delivery_only()
    {
        var at = new DateTime(2026, 11, 1, 10, 0, 0, DateTimeKind.Utc);
        Assert.Equal(at.AddHours(48), OrderIssueRules.ReportUntil(OrderStatus.Delivered, at));
        Assert.Equal(at.AddHours(48), OrderIssueRules.ReportUntil(OrderStatus.Completed, at));
        Assert.Null(OrderIssueRules.ReportUntil(OrderStatus.Ready, at));
        Assert.Null(OrderIssueRules.ReportUntil(OrderStatus.Delivered, null));
    }
}

[Collection("SqlServer")]
public sealed class OrderIssueEfTests
{
    [Fact]
    public async Task A_cash_order_is_reported_once_decided_once_and_the_cash_is_marked_paid()
    {
        if (ConnectionString is null) { return; }
        await using var world = await World.CreateAsync(1, 10);
        var placed = await world.Checkout(world.CustomerIds[0], Commit("k" + Guid.NewGuid().ToString("N")[..12]));
        await using var db = world.Db();
        var organizationId = await db.Stores.Where(store => store.Id == world.Store.Id).Select(store => store.OrganizationId).SingleAsync();
        var data = new EfCommerceStore(db);
        foreach (var step in new[] { OrderStatus.Accepted, OrderStatus.Preparing, OrderStatus.Ready, OrderStatus.Completed })
        {
            await data.TryTransitionOrderAsync(placed.Order!.Id, organizationId, world.Store.Id, step);
        }

        db.ChangeTracker.Clear();
        var order = await db.Orders.AsNoTracking().Include(item => item.Items).SingleAsync(item => item.Id == placed.Order!.Id);
        var store = new EfOrderIssueStore(db);
        var customer = new OrderIssueService(store, new Context(new UserContext(order.UserId, organizationId, null, Role.Customer)));
        var manager = new OrderIssueService(store, new Context(new UserContext(Guid.NewGuid(), organizationId, world.Store.Id, Role.StoreStaff, StaffCategory.StoreManager)));
        var employee = new OrderIssueService(store, new Context(new UserContext(Guid.NewGuid(), organizationId, world.Store.Id, Role.StoreStaff, StaffCategory.StoreEmployee)));
        var line = order.Items[0];
        var request = new ReportIssueRequest("Damaged", [new IssueLineRequest(line.ProductId, line.VariantId, 1)], "Torn packet");

        Assert.NotNull((await customer.MineAsync(order.Id)).Value!.ReportUntil);
        var made = await customer.ReportAsync(order.Id, request);
        Assert.Equal(IssueStatus.Succeeded, made.Status);
        Assert.Equal(IssueStatus.Conflict, (await customer.ReportAsync(order.Id, request)).Status);

        Assert.Contains((await employee.ListAsync()).Value!, item => item.Id == made.Value!.Id && !item.CanDecide);
        Assert.Equal(IssueStatus.Forbidden, (await employee.DecideAsync(made.Value!.Id, new DecideIssueRequest(true))).Status);
        Assert.Equal(IssueStatus.Invalid, (await manager.DecideAsync(made.Value.Id, new DecideIssueRequest(true, made.Value.ClaimedAmount + 1))).Status);
        Assert.Equal(IssueStatus.Invalid, (await manager.DecideAsync(made.Value.Id, new DecideIssueRequest(false))).Status);
        var decided = await manager.DecideAsync(made.Value.Id, new DecideIssueRequest(true, 1, "Sorry"));
        Assert.Equal(("Approved", 1m, "Cash"), (decided.Value!.Status, decided.Value.ApprovedAmount, decided.Value.RefundMethod));
        Assert.Equal(IssueStatus.Conflict, (await manager.DecideAsync(made.Value.Id, new DecideIssueRequest(false, Note: "No"))).Status);

        Assert.Equal(IssueStatus.Succeeded, (await employee.MarkCashPaidAsync(made.Value.Id)).Status);
        Assert.Equal(IssueStatus.Conflict, (await employee.MarkCashPaidAsync(made.Value.Id)).Status);
    }

    private sealed class Context(UserContext context) : ICurrentUserContextResolver
    {
        public Task<UserContext?> ResolveAsync(CancellationToken cancellationToken = default) => Task.FromResult<UserContext?>(context);
    }
}
