using Microsoft.EntityFrameworkCore;
using QuickCommerce.Application.DTOs;
using QuickCommerce.Application.Interfaces;
using QuickCommerce.Application.Services;
using QuickCommerce.Domain;
using QuickCommerce.Infrastructure;
using QuickCommerce.Infrastructure.Persistence;
using Xunit;

namespace QuickCommerce.Tests;

/// <summary>P24: the store removes or reduces items of an order it is packing; the customer is refunded the difference.</summary>
[Collection("SqlServer")]
public sealed class OrderChangeTests
{
    private static readonly Guid Staff = Guid.NewGuid();
    private static readonly DateTime Now = new(2026, 10, 26, 10, 0, 0, DateTimeKind.Utc);

    private static Order Packing(OrderStatus status = OrderStatus.Accepted, decimal? commission = null)
    {
        var order = new Order { OrderNumber = "C-1", UserId = Guid.NewGuid(), StoreId = Guid.NewGuid(), DeliveryAddress = "x", Status = status, DeliveryFee = 25, HandlingFee = 5, PaymentMethod = PaymentMethods.Online };
        order.Items.Add(new OrderItem { ProductId = Guid.NewGuid(), VariantId = Guid.NewGuid(), ProductNameSnapshot = "Milk", VariantLabelSnapshot = "1 l", UnitPrice = 34, Quantity = 3 });
        order.Items.Add(new OrderItem { ProductId = Guid.NewGuid(), VariantId = Guid.NewGuid(), ProductNameSnapshot = "Rice", VariantLabelSnapshot = "1 kg", UnitPrice = 89, Quantity = 2 });
        order.SubtotalAmount = 280;
        order.TotalAmount = 310;
        if (commission is { } percent)
        {
            order.CommissionPercent = percent;
            order.CommissionAmount = CommissionMath.Amount(280, percent);
        }

        return order;
    }

    private static Payment PaidFor(Order order) => new() { OrderId = order.Id, Status = PaymentState.Paid, AmountPaise = 31000, ProviderPaymentId = "pay_1" };

    private static ItemChange To(Order order, int line, int quantity) => new(order.Items[line].ProductId, order.Items[line].VariantId, quantity);

    // ---------------- the rules ----------------

    [Fact]
    public void Reducing_and_removing_lines_updates_the_totals_and_keeps_the_fees()
    {
        var order = Packing(commission: 10);

        var result = OrderChangeRules.Apply(order, null, [To(order, 0, 1), To(order, 1, 0)], "Out of stock", Staff, Now, out var adjustment);

        Assert.Equal(OrderChangeStatus.Succeeded, result.Status);
        Assert.Equal(34m, order.SubtotalAmount); // one milk left
        Assert.Equal(64m, order.TotalAmount); // plus the fees as charged
        Assert.Equal(3.40m, order.CommissionAmount); // commission follows the new product total
        Assert.Equal((280m, 34m), (adjustment!.OldSubtotal, adjustment.NewSubtotal));
        var info = OrderChangeRules.ToInfo(adjustment);
        Assert.Equal([("Milk", 3, 1), ("Rice", 2, 0)], info.Lines.Select(line => (line.Name, line.From, line.To)));
        Assert.Equal("Out of stock", info.Reason);
    }

    [Fact]
    public void A_paid_online_order_queues_a_refund_of_exactly_what_was_removed_and_remembers_it_on_the_payment()
    {
        var order = Packing();
        var payment = PaidFor(order);

        OrderChangeRules.Apply(order, payment, [To(order, 0, 1)], null, Staff, Now, out var adjustment);

        Assert.Equal((6800L, "Pending"), (adjustment!.RefundPaise, adjustment.RefundStatus)); // 2 milk x 34
        Assert.Equal(6800L, payment.PartialRefundedPaise);
    }

    [Fact]
    public void Two_changes_add_up_and_never_refund_more_than_was_removed()
    {
        var order = Packing();
        var payment = PaidFor(order);

        OrderChangeRules.Apply(order, payment, [To(order, 0, 2)], null, Staff, Now, out var first);
        OrderChangeRules.Apply(order, payment, [To(order, 0, 1)], null, Staff, Now.AddMinutes(1), out var second);

        Assert.Equal((3400L, 3400L), (first!.RefundPaise, second!.RefundPaise));
        Assert.Equal(6800L, payment.PartialRefundedPaise);
        Assert.Equal(34m + 178m, order.SubtotalAmount);
    }

    [Fact]
    public void Cash_on_delivery_and_unpaid_orders_have_no_refund()
    {
        var cash = Packing();
        cash.PaymentMethod = PaymentMethods.CashOnDelivery;
        var unpaid = Packing();

        OrderChangeRules.Apply(cash, null, [To(cash, 0, 1)], null, Staff, Now, out var cashChange);
        OrderChangeRules.Apply(unpaid, new Payment { OrderId = unpaid.Id, Status = PaymentState.Created }, [To(unpaid, 0, 1)], null, Staff, Now, out var unpaidChange);

        Assert.Equal((0L, "None"), (cashChange!.RefundPaise, cashChange.RefundStatus));
        Assert.Equal((0L, "None"), (unpaidChange!.RefundPaise, unpaidChange.RefundStatus));
    }

    [Theory]
    [InlineData(OrderStatus.Pending)]
    [InlineData(OrderStatus.Ready)]
    [InlineData(OrderStatus.OutForDelivery)]
    [InlineData(OrderStatus.Delivered)]
    [InlineData(OrderStatus.Cancelled)]
    public void Only_an_accepted_or_packing_order_can_be_changed(OrderStatus status)
    {
        var order = Packing(status);

        var result = OrderChangeRules.Apply(order, null, [To(order, 0, 1)], null, Staff, Now, out var adjustment);

        Assert.Equal(OrderChangeStatus.WrongState, result.Status);
        Assert.Null(adjustment);
        Assert.Equal(3, order.Items[0].Quantity);
    }

    [Fact]
    public void Bad_changes_are_refused_and_change_nothing()
    {
        var order = Packing();
        var unknown = new ItemChange(Guid.NewGuid(), null, 0);

        Assert.Equal(OrderChangeStatus.Invalid, OrderChangeRules.Apply(order, null, [], null, Staff, Now, out _).Status);
        Assert.Equal(OrderChangeStatus.Invalid, OrderChangeRules.Apply(order, null, [unknown], null, Staff, Now, out _).Status);
        Assert.Equal(OrderChangeStatus.Invalid, OrderChangeRules.Apply(order, null, [To(order, 0, 3)], null, Staff, Now, out _).Status); // not lower
        Assert.Equal(OrderChangeStatus.Invalid, OrderChangeRules.Apply(order, null, [To(order, 0, 9)], null, Staff, Now, out _).Status); // an increase
        Assert.Equal(OrderChangeStatus.Invalid, OrderChangeRules.Apply(order, null, [To(order, 0, -1)], null, Staff, Now, out _).Status);
        Assert.Equal(OrderChangeStatus.Invalid, OrderChangeRules.Apply(order, null, [To(order, 0, 1), To(order, 0, 2)], null, Staff, Now, out _).Status); // the same line twice
        Assert.Equal(OrderChangeStatus.Invalid, OrderChangeRules.Apply(order, null, [To(order, 0, 1)], "<b>x</b>", Staff, Now, out _).Status);
        Assert.Equal(OrderChangeStatus.Invalid, OrderChangeRules.Apply(order, null, [To(order, 0, 1)], new string('a', 201), Staff, Now, out _).Status);
        Assert.Equal((3, 2, 280m, 310m), (order.Items[0].Quantity, order.Items[1].Quantity, order.SubtotalAmount, order.TotalAmount));
    }

    [Fact]
    public void Removing_every_item_is_refused_with_a_reason_pointing_to_cancelling()
    {
        var order = Packing();

        var result = OrderChangeRules.Apply(order, null, [To(order, 0, 0), To(order, 1, 0)], null, Staff, Now, out _);

        Assert.Equal((OrderChangeStatus.Invalid, OrderChangeReasons.UseCancel), (result.Status, result.Reason));
        Assert.Equal(280m, order.SubtotalAmount);
    }

    // ---------------- the service ----------------

    private sealed class Who : ICurrentUserContextResolver
    {
        public UserContext? Context { get; set; }
        public Task<UserContext?> ResolveAsync(CancellationToken cancellationToken = default) => Task.FromResult(Context);
    }

    /// <summary>The store's job over the in-memory orders, using the same rules as SQL.</summary>
    private sealed class MemoryChanges(InMemoryCommerceStore data) : IOrderChangeStore
    {
        public List<OrderAdjustment> Adjustments { get; } = [];

        public Task<OrderChangeCommit?> ApplyAsync(Guid orderId, Guid organizationId, Guid? storeId, Guid changedBy, IReadOnlyList<ItemChange> changes, string? reason, DateTime now, CancellationToken cancellationToken = default)
        {
            var order = data.Orders.FirstOrDefault(item => item.Id == orderId && (!storeId.HasValue || item.StoreId == storeId.Value));
            if (order is null)
            {
                return Task.FromResult<OrderChangeCommit?>(null);
            }

            var commit = OrderChangeRules.Apply(order, data.Payments.FirstOrDefault(item => item.OrderId == orderId), changes, reason, changedBy, now, out var adjustment);
            if (adjustment is not null)
            {
                order.Items.RemoveAll(item => item.Quantity == 0);
                Adjustments.Add(adjustment);
            }

            return Task.FromResult<OrderChangeCommit?>(commit);
        }

        public Task<IReadOnlyList<OrderAdjustment>> ListAsync(Guid orderId, CancellationToken cancellationToken = default) => Task.FromResult<IReadOnlyList<OrderAdjustment>>(Adjustments.Where(item => item.OrderId == orderId).ToArray());
        public Task<IReadOnlyList<(OrderAdjustment Adjustment, string ProviderPaymentId)>> GetRefundsToStartAsync(int max, CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<(OrderAdjustment, string)>>(Adjustments.Where(item => item.RefundStatus == "Pending").Select(item => (item, data.Payments.First(payment => payment.OrderId == item.OrderId).ProviderPaymentId!)).ToArray());

        public Task SetRefundStartedAsync(Guid adjustmentId, string refundId, bool processed, CancellationToken cancellationToken = default)
        {
            var item = Adjustments.Single(adjustment => adjustment.Id == adjustmentId);
            item.RefundId = refundId;
            item.RefundStatus = processed ? "Processed" : "Started";
            return Task.CompletedTask;
        }

        public Task SetRefundFailedAsync(Guid adjustmentId, string reason, bool permanent, CancellationToken cancellationToken = default)
        {
            var item = Adjustments.Single(adjustment => adjustment.Id == adjustmentId);
            item.RefundFailure = reason;
            item.RefundStatus = permanent ? "Failed" : "Pending";
            return Task.CompletedTask;
        }

        public Task<IReadOnlyList<(OrderAdjustment Adjustment, string ProviderPaymentId, long RefundedBefore)>> GetStartedRefundsAsync(int max, CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<(OrderAdjustment, string, long)>>(Adjustments.Where(item => item.RefundStatus == "Started")
                .Select(item => (item, data.Payments.First(payment => payment.OrderId == item.OrderId).ProviderPaymentId!, Adjustments.Where(other => other.OrderId == item.OrderId && other.CreatedAt < item.CreatedAt && other.RefundStatus is "Started" or "Processed").Sum(other => other.RefundPaise))).ToArray());

        public Task MarkRefundProcessedAsync(Guid adjustmentId, CancellationToken cancellationToken = default)
        {
            Adjustments.Single(adjustment => adjustment.Id == adjustmentId).RefundStatus = "Processed";
            return Task.CompletedTask;
        }
    }

    private static (OrderChangeService Service, MemoryChanges Store, Who Who, InMemoryCommerceStore Data, Order Order) Rig(OrderStatus status = OrderStatus.Accepted)
    {
        var data = InMemoryCommerceStore.CreateSeeded();
        var store = data.Stores.First();
        var order = Packing(status);
        order.StoreId = store.Id;
        data.Orders.Add(order);
        var who = new Who { Context = new UserContext(Staff, data.Organizations.Single().Id, store.Id, Role.StoreStaff) };
        var changes = new MemoryChanges(data);
        return (new OrderChangeService(changes, who, new TestClock(new DateTimeOffset(Now))), changes, who, data, order);
    }

    [Fact]
    public async Task Store_staff_change_their_own_stores_order()
    {
        var (service, _, _, _, order) = Rig();

        var result = await service.ChangeAsync(order.Id, new ChangeOrderItemsRequest([To(order, 0, 1)], "Milk finished"));

        Assert.Equal(OrderChangeStatus.Succeeded, result.Status);
        Assert.Equal(order.OrderNumber, result.Value!.OrderNumber);
        Assert.Equal(212m, result.Value.NewSubtotal); // 1 milk + 2 rice
        Assert.Equal(242m, result.Value.NewTotal);
        Assert.Equal("Milk finished", result.Value.Change.Reason);
    }

    [Fact]
    public async Task Another_stores_staff_a_customer_and_nobody_are_refused()
    {
        var (service, _, who, data, order) = Rig();

        who.Context = new UserContext(Staff, data.Organizations.Single().Id, data.Stores.Last().Id, Role.StoreStaff);
        Assert.Equal(OrderChangeStatus.NotFound, (await service.ChangeAsync(order.Id, new ChangeOrderItemsRequest([To(order, 0, 1)]))).Status);
        who.Context = new UserContext(Staff, data.Organizations.Single().Id, null, Role.Customer);
        Assert.Equal(OrderChangeStatus.Forbidden, (await service.ChangeAsync(order.Id, new ChangeOrderItemsRequest([To(order, 0, 1)]))).Status);
        who.Context = null;
        Assert.Equal(OrderChangeStatus.Forbidden, (await service.ChangeAsync(order.Id, new ChangeOrderItemsRequest([To(order, 0, 1)]))).Status);
        Assert.Equal(3, order.Items[0].Quantity);
    }

    [Fact]
    public async Task An_administrator_can_change_any_order_and_an_unknown_order_is_not_found()
    {
        var (service, _, who, data, order) = Rig();
        who.Context = new UserContext(Staff, data.Organizations.Single().Id, null, Role.Admin);

        Assert.Equal(OrderChangeStatus.Succeeded, (await service.ChangeAsync(order.Id, new ChangeOrderItemsRequest([To(order, 1, 1)]))).Status);
        Assert.Equal(OrderChangeStatus.NotFound, (await service.ChangeAsync(Guid.NewGuid(), new ChangeOrderItemsRequest([To(order, 0, 1)]))).Status);
    }

    // ---------------- the refund ----------------

    private static async Task<(PaymentRig Rig, MemoryChanges Changes, OrderResponse Order, PaymentMaintenance Maintenance)> PaidOrder()
    {
        var rig = PaymentRig.Create();
        var order = await rig.PlaceOnlineAsync(quantity: 3);
        var paid = await rig.PayAsync(order.Id);
        await rig.Payments.ConfirmAsync(order.Id, paid.Confirm);
        await rig.Data.TryTransitionOrderAsync(order.Id, rig.Data.Organizations.Single().Id, null, OrderStatus.Accepted);
        var changes = new MemoryChanges(rig.Data);
        var maintenance = new PaymentMaintenance(rig.Data, rig.Gateway, rig.Settings, rig.Clock, Microsoft.Extensions.Logging.Abstractions.NullLogger<PaymentMaintenance>.Instance, changes);
        return (rig, changes, order, maintenance);
    }

    [Fact]
    public async Task A_removed_item_on_a_paid_order_is_refunded_through_the_provider_and_confirmed()
    {
        var (rig, changes, order, maintenance) = await PaidOrder();
        var entity = rig.Data.Orders.Single(item => item.Id == order.Id);
        var unit = entity.Items[0].UnitPrice;
        var commit = await changes.ApplyAsync(order.Id, rig.Data.Organizations.Single().Id, null, Staff, [new ItemChange(entity.Items[0].ProductId, entity.Items[0].VariantId, 1)], null, Now);
        Assert.Equal(OrderChangeStatus.Succeeded, commit!.Status);

        await maintenance.StartRefundsAsync();

        var adjustment = Assert.Single(changes.Adjustments);
        Assert.Equal("Processed", adjustment.RefundStatus);
        Assert.Equal((long)(2 * unit * 100), rig.Razorpay.Payments.Values.Single().Refunded);
        Assert.NotNull(adjustment.RefundId);
    }

    [Fact]
    public async Task A_refund_the_provider_reports_pending_is_confirmed_by_the_next_look_and_never_asked_twice()
    {
        var (rig, changes, order, maintenance) = await PaidOrder();
        rig.Razorpay.RefundStatus = "pending";
        var entity = rig.Data.Orders.Single(item => item.Id == order.Id);
        await changes.ApplyAsync(order.Id, rig.Data.Organizations.Single().Id, null, Staff, [new ItemChange(entity.Items[0].ProductId, entity.Items[0].VariantId, 2)], null, Now);

        await maintenance.StartRefundsAsync(); // asks the provider, then sees the refund on the payment and confirms it
        var refundedOnce = rig.Razorpay.Payments.Values.Single().Refunded;
        Assert.Equal("Processed", changes.Adjustments.Single().RefundStatus);

        await maintenance.StartRefundsAsync();

        Assert.Equal(refundedOnce, rig.Razorpay.Payments.Values.Single().Refunded); // not asked twice
        Assert.True(refundedOnce > 0);
    }

    [Fact]
    public async Task A_full_refund_after_a_partial_one_gives_back_only_the_rest()
    {
        var (rig, changes, order, maintenance) = await PaidOrder();
        var entity = rig.Data.Orders.Single(item => item.Id == order.Id);
        await changes.ApplyAsync(order.Id, rig.Data.Organizations.Single().Id, null, Staff, [new ItemChange(entity.Items[0].ProductId, entity.Items[0].VariantId, 1)], null, Now);
        await maintenance.StartRefundsAsync();
        var payment = rig.Data.Payments.Single();
        payment.Status = PaymentState.Refunding;
        payment.RefundId = null;

        await maintenance.StartRefundsAsync();

        Assert.Equal(payment.AmountPaise, rig.Razorpay.Payments.Values.Single().Refunded); // the whole payment, not more
    }

    // ---------------- SQL ----------------

    [Fact]
    public async Task On_SQL_the_change_is_saved_with_its_refund_the_customer_is_told_and_a_stale_order_conflicts()
    {
        if (CheckoutEfIntegrationTests.ConnectionString is null)
        {
            return;
        }

        await using var world = await CheckoutEfIntegrationTests.World.CreateAsync(1, 20, cartQuantity: 3);
        var placed = await world.Checkout(world.CustomerIds[0], CheckoutEfIntegrationTests.Commit(null) with { CommissionPercent = 10m });
        Assert.Equal(CheckoutCommitStatus.Succeeded, placed.Status);
        var orderId = placed.Order!.Id;
        Guid organization;
        await using (var db = world.Db())
        {
            organization = await db.Stores.Where(store => store.Id == world.Store.Id).Select(store => store.OrganizationId).SingleAsync();
            await db.Orders.Where(order => order.Id == orderId).ExecuteUpdateAsync(set => set.SetProperty(order => order.Status, OrderStatus.Accepted).SetProperty(order => order.PaymentMethod, PaymentMethods.Online));
            db.Payments.Add(new Payment { OrderId = orderId, Status = PaymentState.Paid, AmountPaise = 12000, ProviderPaymentId = "pay_sql" });
            await db.SaveChangesAsync();
        }

        OrderChangeCommit? commit;
        await using (var db = world.Db())
        {
            commit = await new EfOrderChangeStore(db).ApplyAsync(orderId, organization, world.Store.Id, Staff, [new ItemChange(world.Product.Id, world.Kilo.Id, 1)], "Short", DateTime.UtcNow);
        }

        Assert.Equal(OrderChangeStatus.Succeeded, commit!.Status);
        await using (var verify = world.Db())
        {
            var order = await verify.Orders.AsNoTracking().Include(item => item.Items).SingleAsync(item => item.Id == orderId);
            var line = Assert.Single(order.Items);
            Assert.Equal(1, line.Quantity);
            Assert.Equal(30m, order.SubtotalAmount); // 1 x 30
            Assert.Equal(3.00m, order.CommissionAmount);
            var adjustment = await verify.OrderAdjustments.AsNoTracking().SingleAsync(item => item.OrderId == orderId);
            Assert.Equal((6000L, "Pending"), (adjustment.RefundPaise, adjustment.RefundStatus)); // 2 x 30 rupees
            Assert.Equal(6000L, (await verify.Payments.AsNoTracking().SingleAsync(item => item.OrderId == orderId)).PartialRefundedPaise);
            Assert.True(await verify.Notifications.AnyAsync(note => note.OrderId == orderId && note.Type == NotificationTypes.OrderChanged));
            var pending = await new EfOrderChangeStore(verify).GetRefundsToStartAsync(10);
            Assert.Contains(pending, entry => entry.Adjustment.Id == adjustment.Id && entry.ProviderPaymentId == "pay_sql");
        }

        // Another store cannot reach it; a second change after the status moved on is refused.
        await using (var db = world.Db())
        {
            Assert.Null(await new EfOrderChangeStore(db).ApplyAsync(orderId, organization, Guid.NewGuid(), Staff, [new ItemChange(world.Product.Id, world.Kilo.Id, 0)], null, DateTime.UtcNow));
            await db.Orders.Where(order => order.Id == orderId).ExecuteUpdateAsync(set => set.SetProperty(order => order.Status, OrderStatus.Ready));
        }

        await using (var db = world.Db())
        {
            var late = await new EfOrderChangeStore(db).ApplyAsync(orderId, organization, world.Store.Id, Staff, [new ItemChange(world.Product.Id, world.Kilo.Id, 0)], null, DateTime.UtcNow);
            Assert.Equal(OrderChangeStatus.WrongState, late!.Status);
        }
    }
}
