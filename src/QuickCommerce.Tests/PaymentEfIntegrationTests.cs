using Microsoft.EntityFrameworkCore;
using QuickCommerce.Application.DTOs;
using QuickCommerce.Application.Interfaces;
using QuickCommerce.Domain;
using QuickCommerce.Infrastructure.Persistence;
using Xunit;

namespace QuickCommerce.Tests;

/// <summary>
/// Phase P7 against a real SQL Server: the amounts, the unique indexes, and the cases where two things reach the same order at once.
/// Opt-in like the other SQL tests (QUICKCOMMERCE_TEST_CONNECTION_STRING, a DISPOSABLE database). Everything added is removed again.
/// </summary>
public sealed class PaymentEfIntegrationTests
{
    private static string? ConnectionString => CheckoutEfIntegrationTests.ConnectionString;

    private static readonly DateTime Now = DateTime.UtcNow;

    private sealed record Placed(Guid OrderId, Guid UserId, Guid OrganizationId, string ProviderOrderId);

    private static async Task<Placed> PlaceOnlineAsync(CheckoutEfIntegrationTests.World world, Guid customer, int expiresInMinutes = 15)
    {
        var commit = CheckoutEfIntegrationTests.Commit(null) with { PaymentMethod = PaymentMethods.Online, PaymentExpiresAt = DateTime.UtcNow.AddMinutes(expiresInMinutes) };
        var placed = await world.Checkout(customer, commit);
        Assert.Equal(OrderStatus.AwaitingPayment, placed.Order!.Status);
        string providerOrderId = "order_" + Guid.NewGuid().ToString("N")[..14];
        await using var db = world.Db();
        var payment = await db.Payments.SingleAsync(item => item.OrderId == placed.Order.Id);
        Assert.True(await new EfPaymentStore(db).AttachProviderOrderAsync(payment.Id, providerOrderId));
        var userId = await db.Customers.Where(item => item.Id == customer).Select(item => item.UserId).SingleAsync();
        var organizationId = await db.Stores.Where(item => item.Id == world.Store.Id).Select(item => item.OrganizationId).SingleAsync();
        return new Placed(placed.Order.Id, userId, organizationId, providerOrderId);
    }

    private static async Task<T> WithStore<T>(CheckoutEfIntegrationTests.World world, Func<EfPaymentStore, Task<T>> work)
    {
        await using var db = world.Db();
        return await work(new EfPaymentStore(db));
    }

    private static async Task WithStore(CheckoutEfIntegrationTests.World world, Func<EfPaymentStore, Task> work)
    {
        await using var db = world.Db();
        await work(new EfPaymentStore(db));
    }

    private static async Task CleanAsync(CheckoutEfIntegrationTests.World world, params string[] eventIds)
    {
        await using var db = world.Db();
        await db.PaymentEvents.Where(item => eventIds.Contains(item.EventId)).ExecuteDeleteAsync();
    }

    [Fact]
    public async Task An_online_order_is_stored_awaiting_payment_with_its_payment_row_in_paise_and_the_stock_held()
    {
        if (ConnectionString is null)
        {
            return;
        }

        await using var world = await CheckoutEfIntegrationTests.World.CreateAsync(customers: 1, stock: 10, cartQuantity: 3);
        var placed = await PlaceOnlineAsync(world, world.CustomerIds.Single());

        Assert.Equal(7, await world.Stock());
        await using var verify = world.Db();
        var order = await verify.Orders.AsNoTracking().SingleAsync(item => item.Id == placed.OrderId);
        var payment = await verify.Payments.AsNoTracking().SingleAsync(item => item.OrderId == placed.OrderId);
        Assert.Equal((OrderStatus.AwaitingPayment, PaymentState.Created, PaymentMethods.Online), (order.Status, order.PaymentStatus, order.PaymentMethod));
        Assert.NotNull(order.PaymentExpiresAt);
        Assert.Equal((long)Math.Round(order.TotalAmount * 100m), payment.AmountPaise);
        Assert.Equal(("INR", PaymentState.Created), (payment.Currency, payment.Status));
    }

    [Fact]
    public async Task Cash_orders_have_no_payment_row_and_the_default_state()
    {
        if (ConnectionString is null)
        {
            return;
        }

        await using var world = await CheckoutEfIntegrationTests.World.CreateAsync(customers: 1, stock: 10, cartQuantity: 1);
        var placed = await world.Checkout(world.CustomerIds.Single(), CheckoutEfIntegrationTests.Commit(null));

        await using var verify = world.Db();
        var order = await verify.Orders.AsNoTracking().SingleAsync(item => item.Id == placed.Order!.Id);
        Assert.Equal((OrderStatus.Pending, PaymentState.NotRequired, null), (order.Status, order.PaymentStatus, order.PaymentExpiresAt));
        Assert.Equal(0, await verify.Payments.CountAsync(item => item.OrderId == order.Id));
    }

    [Fact]
    public async Task Only_one_of_several_requests_can_attach_a_provider_order()
    {
        if (ConnectionString is null)
        {
            return;
        }

        await using var world = await CheckoutEfIntegrationTests.World.CreateAsync(customers: 1, stock: 10, cartQuantity: 1);
        var commit = CheckoutEfIntegrationTests.Commit(null) with { PaymentMethod = PaymentMethods.Online, PaymentExpiresAt = DateTime.UtcNow.AddMinutes(15) };
        var placed = await world.Checkout(world.CustomerIds.Single(), commit);
        Guid paymentId;
        await using (var db = world.Db())
        {
            paymentId = await db.Payments.Where(item => item.OrderId == placed.Order!.Id).Select(item => item.Id).SingleAsync();
        }

        var results = await Task.WhenAll(Enumerable.Range(0, 6).Select(index => WithStore(world, store => store.AttachProviderOrderAsync(paymentId, $"order_race_{Guid.NewGuid():N}"[..24]))));

        Assert.Equal(1, results.Count(won => won));
    }

    [Fact]
    public async Task The_app_confirming_and_the_notification_arriving_together_make_one_payment_and_one_note()
    {
        if (ConnectionString is null)
        {
            return;
        }

        for (var round = 0; round < 4; round++)
        {
            await using var world = await CheckoutEfIntegrationTests.World.CreateAsync(customers: 1, stock: 10, cartQuantity: 2);
            var placed = await PlaceOnlineAsync(world, world.CustomerIds.Single());
            var paise = await world.Db().Payments.Select(item => item.AmountPaise).SingleAsync();
            var paymentId = "pay_" + Guid.NewGuid().ToString("N")[..14];

            var outcomes = await Task.WhenAll(Enumerable.Range(0, 5).Select(_ => WithStore(world, store => store.ApplyCapturedAsync(placed.ProviderOrderId, paymentId, paise, DateTime.UtcNow))));

            Assert.Equal(1, outcomes.Count(item => item == CapturedOutcome.Paid));
            Assert.All(outcomes, item => Assert.True(item is CapturedOutcome.Paid or CapturedOutcome.AlreadyPaid, item.ToString()));
            await using var verify = world.Db();
            var order = await verify.Orders.AsNoTracking().Include(item => item.StatusHistory).SingleAsync(item => item.Id == placed.OrderId);
            Assert.Equal((OrderStatus.Pending, PaymentState.Paid, 2), (order.Status, order.PaymentStatus, order.StatusHistory.Count));
            Assert.Equal(1, await verify.Notifications.CountAsync(item => item.OrderId == placed.OrderId));
            Assert.Equal(paymentId, (await verify.Payments.AsNoTracking().SingleAsync(item => item.OrderId == placed.OrderId)).ProviderPaymentId);
        }
    }

    [Fact]
    public async Task A_payment_landing_while_the_customer_cancels_ends_with_one_outcome_and_the_money_never_kept_for_nothing()
    {
        if (ConnectionString is null)
        {
            return;
        }

        for (var round = 0; round < 6; round++)
        {
            await using var world = await CheckoutEfIntegrationTests.World.CreateAsync(customers: 1, stock: 10, cartQuantity: 2);
            var placed = await PlaceOnlineAsync(world, world.CustomerIds.Single());
            var paise = await world.Db().Payments.Select(item => item.AmountPaise).SingleAsync();
            var providerPaymentId = "pay_" + Guid.NewGuid().ToString("N")[..14];

            var capture = WithStore(world, store => store.ApplyCapturedAsync(placed.ProviderOrderId, providerPaymentId, paise, DateTime.UtcNow));
            var cancel = Task.Run(async () =>
            {
                await using var db = world.Db();
                return await new EfCommerceStore(db).TryCancelCustomerOrderAsync(placed.OrderId, placed.UserId, placed.OrganizationId);
            });
            await Task.WhenAll(capture, cancel);

            await using var verify = world.Db();
            var order = await verify.Orders.AsNoTracking().SingleAsync(item => item.Id == placed.OrderId);
            var payment = await verify.Payments.AsNoTracking().SingleAsync(item => item.OrderId == placed.OrderId);
            if (order.Status == OrderStatus.Cancelled)
            {
                // Cancelled: whatever was paid is queued to go back, nothing is kept, and the items are free again.
                Assert.Equal(10, await world.Stock());
                Assert.True(payment.Status is PaymentState.Failed or PaymentState.Refunding, payment.Status.ToString());
                Assert.Equal(payment.Status == PaymentState.Refunding, payment.ProviderPaymentId == providerPaymentId);
            }
            else
            {
                Assert.Equal((OrderStatus.Pending, PaymentState.Paid, 8), (order.Status, payment.Status, await world.Stock()));
            }
        }
    }

    [Fact]
    public async Task A_payment_landing_while_the_hold_expires_either_pays_the_order_or_queues_a_refund_never_both_never_neither()
    {
        if (ConnectionString is null)
        {
            return;
        }

        for (var round = 0; round < 6; round++)
        {
            await using var world = await CheckoutEfIntegrationTests.World.CreateAsync(customers: 1, stock: 10, cartQuantity: 2);
            var placed = await PlaceOnlineAsync(world, world.CustomerIds.Single(), expiresInMinutes: -1);
            var paise = await world.Db().Payments.Select(item => item.AmountPaise).SingleAsync();

            var capture = WithStore(world, store => store.ApplyCapturedAsync(placed.ProviderOrderId, "pay_" + Guid.NewGuid().ToString("N")[..14], paise, DateTime.UtcNow));
            var release = WithStore(world, store => store.ReleaseExpiredAsync(DateTime.UtcNow, 50));
            await Task.WhenAll(capture, release);

            await using var verify = world.Db();
            var order = await verify.Orders.AsNoTracking().SingleAsync(item => item.Id == placed.OrderId);
            var payment = await verify.Payments.AsNoTracking().SingleAsync(item => item.OrderId == placed.OrderId);
            if (order.Status == OrderStatus.Cancelled)
            {
                Assert.Equal(10, await world.Stock());
                Assert.True(payment.Status is PaymentState.Failed or PaymentState.Refunding, payment.Status.ToString());
            }
            else
            {
                Assert.Equal((OrderStatus.Pending, PaymentState.Paid), (order.Status, payment.Status));
                Assert.Equal(8, await world.Stock());
            }
        }
    }

    [Fact]
    public async Task Releasing_from_several_workers_at_once_returns_the_stock_once_and_leaves_future_holds_alone()
    {
        if (ConnectionString is null)
        {
            return;
        }

        await using var world = await CheckoutEfIntegrationTests.World.CreateAsync(customers: 2, stock: 10, cartQuantity: 3);
        var expired = await PlaceOnlineAsync(world, world.CustomerIds[0], expiresInMinutes: -2);
        var live = await PlaceOnlineAsync(world, world.CustomerIds[1], expiresInMinutes: 10);
        Assert.Equal(4, await world.Stock());

        var results = await Task.WhenAll(Enumerable.Range(0, 5).Select(_ => WithStore(world, store => store.ReleaseExpiredAsync(DateTime.UtcNow, 50))));

        Assert.Equal(1, results.Sum(item => item.Count(id => id == expired.OrderId)));
        Assert.Equal(7, await world.Stock());
        await using var verify = world.Db();
        var expiredRow = await verify.Orders.AsNoTracking().SingleAsync(item => item.Id == expired.OrderId);
        Assert.Equal((OrderStatus.Cancelled, PaymentState.Failed), (expiredRow.Status, expiredRow.PaymentStatus));
        Assert.Equal(OrderStatus.AwaitingPayment, (await verify.Orders.AsNoTracking().SingleAsync(item => item.Id == live.OrderId)).Status);
        Assert.Equal(1, await verify.Notifications.CountAsync(item => item.OrderId == expired.OrderId));
    }

    [Fact]
    public async Task A_paid_order_the_shop_rejects_and_a_paid_order_cancelled_each_queue_one_refund_and_only_the_cancel_returns_stock()
    {
        if (ConnectionString is null)
        {
            return;
        }

        await using var world = await CheckoutEfIntegrationTests.World.CreateAsync(customers: 2, stock: 10, cartQuantity: 2);
        var cancelled = await PlaceOnlineAsync(world, world.CustomerIds[0]);
        var rejected = await PlaceOnlineAsync(world, world.CustomerIds[1]);
        foreach (var placed in new[] { cancelled, rejected })
        {
            var paise = await world.Db().Payments.Where(item => item.OrderId == placed.OrderId).Select(item => item.AmountPaise).SingleAsync();
            await WithStore(world, store => store.ApplyCapturedAsync(placed.ProviderOrderId, "pay_" + Guid.NewGuid().ToString("N")[..14], paise, DateTime.UtcNow));
        }

        await using (var db = world.Db())
        {
            await new EfCommerceStore(db).TryCancelCustomerOrderAsync(cancelled.OrderId, cancelled.UserId, cancelled.OrganizationId);
        }

        await using (var db = world.Db())
        {
            await new EfCommerceStore(db).TryTransitionOrderAsync(rejected.OrderId, rejected.OrganizationId, null, OrderStatus.Rejected);
        }

        await using var verify = world.Db();
        var states = await verify.Payments.AsNoTracking().Where(item => item.OrderId == cancelled.OrderId || item.OrderId == rejected.OrderId).Select(item => item.Status).ToListAsync();
        Assert.All(states, state => Assert.Equal(PaymentState.Refunding, state));
        Assert.Equal(8, await world.Stock());
        var queue = await WithStore(world, store => store.GetRefundsToStartAsync(50));
        Assert.Equal(2, queue.Count(item => item.OrderId == cancelled.OrderId || item.OrderId == rejected.OrderId));
    }

    [Fact]
    public async Task A_refund_result_closes_the_payment_once_and_a_second_result_changes_nothing()
    {
        if (ConnectionString is null)
        {
            return;
        }

        await using var world = await CheckoutEfIntegrationTests.World.CreateAsync(customers: 1, stock: 10, cartQuantity: 1);
        var placed = await PlaceOnlineAsync(world, world.CustomerIds.Single());
        var providerPaymentId = "pay_" + Guid.NewGuid().ToString("N")[..14];
        var paise = await world.Db().Payments.Select(item => item.AmountPaise).SingleAsync();
        await WithStore(world, store => store.ApplyCapturedAsync(placed.ProviderOrderId, providerPaymentId, paise, DateTime.UtcNow));
        await using (var db = world.Db())
        {
            await new EfCommerceStore(db).TryCancelCustomerOrderAsync(placed.OrderId, placed.UserId, placed.OrganizationId);
        }

        var payment = (await WithStore(world, store => store.GetRefundsToStartAsync(50))).Single(item => item.OrderId == placed.OrderId);
        await WithStore(world, store => store.SetRefundStartedAsync(payment.Id, "rfnd_1", processed: false, DateTime.UtcNow));
        await Task.WhenAll(Enumerable.Range(0, 4).Select(_ => WithStore(world, store => store.ApplyRefundResultAsync(providerPaymentId, "rfnd_1", processed: true, DateTime.UtcNow))));

        await using var verify = world.Db();
        Assert.Equal((PaymentState.Refunded, PaymentState.Refunded), (await verify.Payments.Where(item => item.OrderId == placed.OrderId).Select(item => item.Status).SingleAsync(), (await verify.Orders.SingleAsync(item => item.Id == placed.OrderId)).PaymentStatus));
        Assert.Equal(1, await verify.Notifications.CountAsync(item => item.OrderId == placed.OrderId && item.Title == "Refund processed"));
        var left = await WithStore(world, store => store.GetRefundsToStartAsync(50));
        Assert.DoesNotContain(left, item => item.OrderId == placed.OrderId);
    }

    [Fact]
    public async Task The_same_provider_event_is_recorded_once_even_when_it_arrives_many_times_at_once()
    {
        if (ConnectionString is null)
        {
            return;
        }

        await using var world = await CheckoutEfIntegrationTests.World.CreateAsync(customers: 1, stock: 10, cartQuantity: 1);
        var eventId = "evt_" + Guid.NewGuid().ToString("N");
        try
        {
            var results = await Task.WhenAll(Enumerable.Range(0, 6).Select(_ => WithStore(world, store => store.TryRecordEventAsync("Razorpay", eventId, "payment.captured", DateTime.UtcNow))));

            Assert.Equal(1, results.Count(item => item));
        }
        finally
        {
            await CleanAsync(world, eventId);
        }
    }

    [Fact]
    public async Task The_database_itself_refuses_a_second_payment_for_an_order_a_reused_provider_order_and_a_zero_amount()
    {
        if (ConnectionString is null)
        {
            return;
        }

        await using var world = await CheckoutEfIntegrationTests.World.CreateAsync(customers: 2, stock: 10, cartQuantity: 1);
        var first = await PlaceOnlineAsync(world, world.CustomerIds[0]);
        var second = await PlaceOnlineAsync(world, world.CustomerIds[1]);

        await using (var db = world.Db())
        {
            db.Payments.Add(new Payment { OrderId = first.OrderId, AmountPaise = 100 });
            await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync());
        }

        await using (var db = world.Db())
        {
            var payment = await db.Payments.SingleAsync(item => item.OrderId == second.OrderId);
            payment.ProviderOrderId = first.ProviderOrderId;
            await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync());
        }

        await using (var db = world.Db())
        {
            var payment = await db.Payments.SingleAsync(item => item.OrderId == second.OrderId);
            payment.AmountPaise = 0;
            await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync());
        }
    }

    [Fact]
    public async Task The_shop_sees_nothing_of_an_unpaid_order_until_it_is_paid()
    {
        if (ConnectionString is null)
        {
            return;
        }

        await using var world = await CheckoutEfIntegrationTests.World.CreateAsync(customers: 1, stock: 10, cartQuantity: 1);
        var placed = await PlaceOnlineAsync(world, world.CustomerIds.Single());
        var storeIds = new HashSet<Guid> { world.Store.Id };

        await using (var db = world.Db())
        {
            var store = new EfCommerceStore(db);
            Assert.DoesNotContain(await store.GetScopedOrdersAsync(placed.OrganizationId, storeIds, true), order => order.Id == placed.OrderId);
            Assert.DoesNotContain(await store.GetScopedOrderSummariesAsync(placed.OrganizationId, storeIds, true), order => order.Id == placed.OrderId);
            Assert.Null(await store.GetOrderAsync(placed.OrderId));
            Assert.NotEqual(OrderLifecycleStatus.Succeeded, (await store.TryTransitionOrderAsync(placed.OrderId, placed.OrganizationId, null, OrderStatus.Accepted)).Status);
        }

        var paise = await world.Db().Payments.Select(item => item.AmountPaise).SingleAsync();
        await WithStore(world, store => store.ApplyCapturedAsync(placed.ProviderOrderId, "pay_" + Guid.NewGuid().ToString("N")[..14], paise, DateTime.UtcNow));

        await using var after = world.Db();
        var paid = new EfCommerceStore(after);
        Assert.Contains(await paid.GetScopedOrdersAsync(placed.OrganizationId, storeIds, true), order => order.Id == placed.OrderId);
        Assert.Equal(OrderLifecycleStatus.Succeeded, (await paid.TryTransitionOrderAsync(placed.OrderId, placed.OrganizationId, null, OrderStatus.Accepted)).Status);
    }
}
