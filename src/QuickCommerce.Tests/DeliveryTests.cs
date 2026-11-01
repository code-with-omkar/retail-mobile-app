using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using QuickCommerce.Api.Controllers;
using QuickCommerce.Application.DTOs;
using QuickCommerce.Application.Interfaces;
using QuickCommerce.Application.Services;
using QuickCommerce.Domain;
using QuickCommerce.Infrastructure;
using QuickCommerce.Infrastructure.Persistence;
using Xunit;

namespace QuickCommerce.Tests;

/// <summary>Phase P12: store staff give a ready order to a delivery partner; the partner picks it up and completes it with the customer's code.</summary>
public sealed class DeliveryTests
{
    private sealed class FixedUser(UserContext? context) : ICurrentUserContextResolver
    {
        public UserContext? Context { get; set; } = context;
        public Task<UserContext?> ResolveAsync(CancellationToken cancellationToken = default) => Task.FromResult(Context);
    }

    private sealed class Rig
    {
        public required InMemoryCommerceStore Data { get; init; }
        public required TestClock Clock { get; init; }
        public required FixedUser Who { get; init; }
        public required Guid Organization { get; init; }
        public required Store A { get; init; }
        public required Store B { get; init; }
        public required User Rina { get; init; }
        public required User Sam { get; init; }
        public required User OtherStorePartner { get; init; }
        public required User Customer { get; init; }
        public required DeliveryService Service { get; init; }

        public void SignInStaff(Store? store, Role role = Role.StoreStaff) => Who.Context = new UserContext(Guid.NewGuid(), Organization, store?.Id, role);
        public void SignInPartner(User partner) => Who.Context = new UserContext(partner.Id, Organization, partner.StoreId, Role.DeliveryPartner);
    }

    private static Rig Create()
    {
        var data = InMemoryCommerceStore.CreateSeeded();
        var stores = data.Stores.Take(2).ToArray();
        var organization = data.Organizations.Single().Id;
        User Partner(string name, string phone, Store store, bool active = true)
        {
            var user = new User { ExternalSubject = name.ToLowerInvariant() + "@partner.test", DisplayName = name + " Patil", FirstName = name, PhoneNumber = phone, OrganizationId = organization, StoreId = store.Id, Role = Role.DeliveryPartner, IsActive = active };
            data.Users.Add(user);
            return user;
        }

        var clock = new TestClock(new DateTimeOffset(2026, 10, 12, 4, 0, 0, TimeSpan.Zero));
        var who = new FixedUser(null);
        return new Rig
        {
            Data = data,
            Clock = clock,
            Who = who,
            Organization = organization,
            A = stores[0],
            B = stores[1],
            Rina = Partner("Rina", "9000000001", stores[0]),
            Sam = Partner("Sam", "9000000002", stores[0]),
            OtherStorePartner = Partner("Omar", "9000000003", stores[1]),
            Customer = data.Users.First(user => user.Role == Role.Customer),
            Service = new DeliveryService(data, who, clock)
        };
    }

    private static Order AddOrder(Rig rig, OrderStatus status = OrderStatus.Ready, string method = PaymentMethods.CashOnDelivery, decimal total = 250, Store? store = null)
    {
        var order = new Order
        {
            OrderNumber = "T-" + Guid.NewGuid().ToString("N")[..6],
            UserId = rig.Customer.Id,
            StoreId = (store ?? rig.A).Id,
            TotalAmount = total,
            Status = status,
            PaymentMethod = method,
            DeliveryAddress = "Flat 4, Sector 20",
            ReceiverName = "Asha",
            ReceiverPhone = "9111111111"
        };
        order.StatusHistory.Add(new OrderStatusHistory { Status = status });
        rig.Data.Orders.Add(order);
        return order;
    }

    private static int Notes(Rig rig, Order order, string type) => rig.Data.Notifications.Count(note => note.OrderId == order.Id && note.Type == type);

    private static async Task<DeliveryResult<OrderResponse>> Assign(Rig rig, Order order, User partner, Store? staffStore = null)
    {
        rig.SignInStaff(staffStore ?? rig.A);
        return await rig.Service.AssignAsync(order.Id, new AssignDeliveryRequest(partner.Id));
    }

    private static async Task<Order> ReadyAssigned(Rig rig, User? partner = null, string method = PaymentMethods.CashOnDelivery, decimal total = 250)
    {
        var order = AddOrder(rig, method: method, total: total);
        Assert.Equal(DeliveryResultStatus.Succeeded, (await Assign(rig, order, partner ?? rig.Rina)).Status);
        return order;
    }

    private static async Task<Order> OutForDelivery(Rig rig, User? partner = null, string method = PaymentMethods.CashOnDelivery, decimal total = 250)
    {
        var order = await ReadyAssigned(rig, partner, method, total);
        rig.SignInPartner(partner ?? rig.Rina);
        Assert.Equal(DeliveryResultStatus.Succeeded, (await rig.Service.PickUpAsync(order.Id)).Status);
        return order;
    }

    // ---------------- giving an order to a partner ----------------

    [Fact]
    public async Task Staff_see_only_the_active_delivery_partners_of_their_own_store_with_how_busy_they_are()
    {
        var rig = Create();
        rig.Data.Users.Add(new User { ExternalSubject = "gone@partner.test", DisplayName = "Gone", OrganizationId = rig.Organization, StoreId = rig.A.Id, Role = Role.DeliveryPartner, IsActive = false });
        await ReadyAssigned(rig, rig.Rina);

        rig.SignInStaff(rig.A);
        var list = (await rig.Service.ListPartnersAsync(null)).Value!;

        Assert.Equal(["Rina Patil", "Sam Patil"], list.Select(item => item.Name));
        Assert.Equal([1, 0], list.Select(item => item.ActiveDeliveries));
        Assert.Equal("9000000001", list[0].Phone);
    }

    [Fact]
    public async Task An_administrator_must_name_the_store_and_a_partner_or_customer_cannot_list_partners()
    {
        var rig = Create();
        rig.SignInStaff(null, Role.Admin);
        Assert.Equal(DeliveryResultStatus.Invalid, (await rig.Service.ListPartnersAsync(null)).Status);
        Assert.Single((await rig.Service.ListPartnersAsync(rig.B.Id)).Value!);

        rig.SignInPartner(rig.Rina);
        Assert.Equal(DeliveryResultStatus.Forbidden, (await rig.Service.ListPartnersAsync(rig.A.Id)).Status);
        rig.Who.Context = new UserContext(rig.Customer.Id, rig.Organization, null, Role.Customer);
        Assert.Equal(DeliveryResultStatus.Forbidden, (await rig.Service.ListPartnersAsync(rig.A.Id)).Status);
    }

    [Fact]
    public async Task A_ready_order_is_given_to_a_partner_of_its_store_and_gets_a_four_digit_code()
    {
        var rig = Create();
        var order = AddOrder(rig);

        var result = await Assign(rig, order, rig.Rina);

        Assert.Equal(DeliveryResultStatus.Succeeded, result.Status);
        Assert.Equal("Rina Patil", result.Value!.DeliveryPartnerName);
        Assert.Equal(rig.Rina.Id, order.DeliveryPartnerId);
        Assert.Matches("^[0-9]{4}$", order.DeliveryOtp!);
        Assert.Null(result.Value.DeliveryOtp);
    }

    [Fact]
    public async Task Only_a_ready_order_can_be_given_and_only_to_an_active_partner_of_the_same_store()
    {
        var rig = Create();
        foreach (var status in new[] { OrderStatus.Pending, OrderStatus.Accepted, OrderStatus.Preparing, OrderStatus.Delivered, OrderStatus.Cancelled })
        {
            Assert.Equal(DeliveryResultStatus.WrongState, (await Assign(rig, AddOrder(rig, status), rig.Rina)).Status);
        }

        var ready = AddOrder(rig);
        Assert.Equal(DeliveryResultStatus.Invalid, (await Assign(rig, ready, rig.OtherStorePartner)).Status);
        Assert.Equal(DeliveryResultStatus.Invalid, (await Assign(rig, ready, rig.Customer)).Status);
        rig.Rina.IsActive = false;
        Assert.Equal(DeliveryResultStatus.Invalid, (await Assign(rig, ready, rig.Rina)).Status);
        Assert.Null(ready.DeliveryPartnerId);
    }

    [Fact]
    public async Task Staff_of_another_store_cannot_give_the_order_but_an_administrator_can()
    {
        var rig = Create();
        var order = AddOrder(rig);

        Assert.Equal(DeliveryResultStatus.NotFound, (await Assign(rig, order, rig.Rina, rig.B)).Status);
        Assert.Null(order.DeliveryPartnerId);

        rig.SignInStaff(null, Role.Admin);
        Assert.Equal(DeliveryResultStatus.Succeeded, (await rig.Service.AssignAsync(order.Id, new AssignDeliveryRequest(rig.Rina.Id))).Status);

        rig.SignInPartner(rig.Rina);
        Assert.Equal(DeliveryResultStatus.Forbidden, (await rig.Service.AssignAsync(order.Id, new AssignDeliveryRequest(rig.Rina.Id))).Status);
    }

    [Fact]
    public async Task Giving_it_again_changes_nothing_and_changing_partner_while_ready_keeps_the_same_code()
    {
        var rig = Create();
        var order = await ReadyAssigned(rig, rig.Rina);
        var code = order.DeliveryOtp;

        Assert.Equal(DeliveryResultStatus.Succeeded, (await Assign(rig, order, rig.Rina)).Status);
        Assert.Equal(code, order.DeliveryOtp);

        Assert.Equal(DeliveryResultStatus.Succeeded, (await Assign(rig, order, rig.Sam)).Status);
        Assert.Equal((rig.Sam.Id, code), (order.DeliveryPartnerId, order.DeliveryOtp));
    }

    [Fact]
    public async Task Once_it_is_out_for_delivery_it_cannot_be_given_to_someone_else()
    {
        var rig = Create();
        var order = await OutForDelivery(rig, rig.Rina);

        Assert.Equal(DeliveryResultStatus.WrongState, (await Assign(rig, order, rig.Sam)).Status);
        Assert.Equal(rig.Rina.Id, order.DeliveryPartnerId);
    }

    [Fact]
    public async Task Once_a_partner_holds_the_order_staff_cannot_complete_it_themselves()
    {
        var rig = Create();
        var held = await ReadyAssigned(rig);
        var free = AddOrder(rig);

        Assert.Equal(OrderLifecycleStatus.InvalidTransition, (await rig.Data.TryTransitionOrderAsync(held.Id, rig.Organization, rig.A.Id, OrderStatus.Completed)).Status);
        Assert.Equal(OrderLifecycleStatus.Succeeded, (await rig.Data.TryTransitionOrderAsync(free.Id, rig.Organization, rig.A.Id, OrderStatus.Completed)).Status);
    }

    // ---------------- the partner's own deliveries ----------------

    [Fact]
    public async Task A_partner_sees_only_their_own_deliveries_with_the_address_contact_and_cash_to_collect()
    {
        var rig = Create();
        var mine = await ReadyAssigned(rig, rig.Rina, total: 250);
        var online = await ReadyAssigned(rig, rig.Rina, PaymentMethods.Online, 90);
        await ReadyAssigned(rig, rig.Sam);

        rig.SignInPartner(rig.Rina);
        var list = (await rig.Service.MineAsync()).Value!;

        Assert.Equal(2, list.Count);
        var cash = list.Single(item => item.OrderId == mine.Id);
        Assert.Equal(("Flat 4, Sector 20", "Asha", "9111111111", 250m), (cash.DeliveryAddress, cash.ReceiverName, cash.ReceiverPhone, cash.CashToCollect));
        Assert.Equal(0m, list.Single(item => item.OrderId == online.Id).CashToCollect);
    }

    [Fact]
    public async Task Delivered_orders_stay_on_the_list_for_a_day_and_then_drop_off()
    {
        var rig = Create();
        var order = await OutForDelivery(rig, rig.Rina);
        rig.SignInPartner(rig.Rina);
        await rig.Service.DeliverAsync(order.Id, new DeliverRequest(order.DeliveryOtp!, 250));

        Assert.Single((await rig.Service.MineAsync()).Value!);
        rig.Clock.Advance(TimeSpan.FromHours(25));
        Assert.Empty((await rig.Service.MineAsync()).Value!);
    }

    [Fact]
    public async Task Only_a_delivery_partner_has_deliveries()
    {
        var rig = Create();
        rig.SignInStaff(rig.A);
        Assert.Equal(DeliveryResultStatus.Forbidden, (await rig.Service.MineAsync()).Status);
        Assert.Equal(DeliveryResultStatus.Forbidden, (await rig.Service.PickUpAsync(Guid.NewGuid())).Status);
        Assert.Equal(DeliveryResultStatus.Forbidden, (await rig.Service.DeliverAsync(Guid.NewGuid(), new DeliverRequest("1234"))).Status);
    }

    // ---------------- picking up ----------------

    [Fact]
    public async Task Picking_up_puts_the_order_out_for_delivery_without_ringing_the_bell()
    {
        var rig = Create();
        var order = await ReadyAssigned(rig);
        rig.SignInPartner(rig.Rina);

        var first = await rig.Service.PickUpAsync(order.Id);
        var again = await rig.Service.PickUpAsync(order.Id);

        Assert.Equal(OrderStatus.OutForDelivery, first.Value!.Status);
        Assert.Equal(DeliveryResultStatus.Succeeded, again.Status);
        Assert.Equal(0, Notes(rig, order, NotificationTypes.OutForDelivery));
        Assert.Equal([OrderStatus.Ready, OrderStatus.OutForDelivery], order.StatusHistory.Select(history => history.Status));
        Assert.NotNull(order.PickedUpAt);
    }

    [Fact]
    public async Task Another_partner_cannot_pick_up_or_complete_someone_elses_order()
    {
        var rig = Create();
        var order = await OutForDelivery(rig, rig.Rina);

        rig.SignInPartner(rig.Sam);
        Assert.Equal(DeliveryResultStatus.NotFound, (await rig.Service.PickUpAsync(order.Id)).Status);
        Assert.Equal(DeliveryResultStatus.NotFound, (await rig.Service.DeliverAsync(order.Id, new DeliverRequest(order.DeliveryOtp!, 250))).Status);
        Assert.Equal(OrderStatus.OutForDelivery, order.Status);
    }

    // ---------------- delivering with the customer's code ----------------

    [Fact]
    public async Task The_right_code_and_the_exact_cash_complete_the_delivery_and_tell_the_customer()
    {
        var rig = Create();
        var order = await OutForDelivery(rig, total: 250);
        rig.SignInPartner(rig.Rina);

        var done = await rig.Service.DeliverAsync(order.Id, new DeliverRequest(order.DeliveryOtp!, 250));

        Assert.Equal(DeliveryResultStatus.Succeeded, done.Status);
        Assert.Equal((OrderStatus.Delivered, 250m), (order.Status, order.CashCollected));
        Assert.NotNull(order.DeliveredAt);
        Assert.Equal(1, Notes(rig, order, NotificationTypes.OrderDelivered));
    }

    [Fact]
    public async Task Delivering_again_changes_nothing_and_does_not_notify_twice()
    {
        var rig = Create();
        var order = await OutForDelivery(rig);
        rig.SignInPartner(rig.Rina);
        await rig.Service.DeliverAsync(order.Id, new DeliverRequest(order.DeliveryOtp!, 250));

        var again = await rig.Service.DeliverAsync(order.Id, new DeliverRequest(order.DeliveryOtp!, 250));

        Assert.Equal(DeliveryResultStatus.Succeeded, again.Status);
        Assert.Equal(1, Notes(rig, order, NotificationTypes.OrderDelivered));
        Assert.Equal(1, order.StatusHistory.Count(history => history.Status == OrderStatus.Delivered));
    }

    [Fact]
    public async Task A_wrong_code_does_not_deliver_and_is_counted()
    {
        var rig = Create();
        var order = await OutForDelivery(rig);
        var wrong = order.DeliveryOtp == "0000" ? "1111" : "0000";
        rig.SignInPartner(rig.Rina);

        var result = await rig.Service.DeliverAsync(order.Id, new DeliverRequest(wrong, 250));

        Assert.Equal(DeliveryResultStatus.WrongOtp, result.Status);
        Assert.Equal((OrderStatus.OutForDelivery, 1), (order.Status, order.OtpFailures));
        Assert.Equal(0, Notes(rig, order, NotificationTypes.OrderDelivered));
    }

    [Fact]
    public async Task Five_wrong_codes_lock_the_order_for_fifteen_minutes_even_for_the_right_code()
    {
        var rig = Create();
        var order = await OutForDelivery(rig);
        var wrong = order.DeliveryOtp == "0000" ? "1111" : "0000";
        rig.SignInPartner(rig.Rina);

        for (var attempt = 1; attempt <= 4; attempt++)
        {
            Assert.Equal(DeliveryResultStatus.WrongOtp, (await rig.Service.DeliverAsync(order.Id, new DeliverRequest(wrong, 250))).Status);
        }

        Assert.Equal(DeliveryResultStatus.Locked, (await rig.Service.DeliverAsync(order.Id, new DeliverRequest(wrong, 250))).Status);
        Assert.Equal(DeliveryResultStatus.Locked, (await rig.Service.DeliverAsync(order.Id, new DeliverRequest(order.DeliveryOtp!, 250))).Status);

        rig.Clock.Advance(TimeSpan.FromMinutes(16));
        Assert.Equal(DeliveryResultStatus.Succeeded, (await rig.Service.DeliverAsync(order.Id, new DeliverRequest(order.DeliveryOtp!, 250))).Status);
    }

    [Theory]
    [InlineData("")]
    [InlineData("123")]
    [InlineData("12345")]
    [InlineData("12a4")]
    [InlineData("    ")]
    public async Task A_code_that_is_not_four_digits_is_refused_before_it_is_counted(string code)
    {
        var rig = Create();
        var order = await OutForDelivery(rig);
        rig.SignInPartner(rig.Rina);

        Assert.Equal(DeliveryResultStatus.Invalid, (await rig.Service.DeliverAsync(order.Id, new DeliverRequest(code, 250))).Status);
        Assert.Equal(0, order.OtpFailures);
    }

    [Fact]
    public async Task Cash_on_delivery_needs_exactly_the_total_and_an_online_order_needs_none()
    {
        var rig = Create();
        var cash = await OutForDelivery(rig, total: 250);
        var online = await OutForDelivery(rig, method: PaymentMethods.Online, total: 90);
        rig.SignInPartner(rig.Rina);

        foreach (var wrong in new[] { 0m, 249m, 251m })
        {
            Assert.Equal(DeliveryResultStatus.Invalid, (await rig.Service.DeliverAsync(cash.Id, new DeliverRequest(cash.DeliveryOtp!, wrong))).Status);
        }

        Assert.Equal(DeliveryResultStatus.Invalid, (await rig.Service.DeliverAsync(online.Id, new DeliverRequest(online.DeliveryOtp!, 90))).Status);
        Assert.Equal(DeliveryResultStatus.Invalid, (await rig.Service.DeliverAsync(cash.Id, new DeliverRequest(cash.DeliveryOtp!, -1))).Status);
        Assert.Equal(DeliveryResultStatus.Succeeded, (await rig.Service.DeliverAsync(online.Id, new DeliverRequest(online.DeliveryOtp!, 0))).Status);
        Assert.Equal(DeliveryResultStatus.Succeeded, (await rig.Service.DeliverAsync(cash.Id, new DeliverRequest(cash.DeliveryOtp!, 250))).Status);
        Assert.Equal(0m, online.CashCollected);
    }

    [Fact]
    public async Task An_order_cannot_be_delivered_before_it_is_picked_up()
    {
        var rig = Create();
        var order = await ReadyAssigned(rig);
        rig.SignInPartner(rig.Rina);

        Assert.Equal(DeliveryResultStatus.WrongState, (await rig.Service.DeliverAsync(order.Id, new DeliverRequest(order.DeliveryOtp!, 250))).Status);
        Assert.Equal(OrderStatus.Ready, order.Status);
    }

    // ---------------- what the customer and staff see ----------------

    [Fact]
    public async Task The_customer_sees_the_partner_and_the_code_only_while_the_order_is_out_for_delivery()
    {
        var rig = Create();
        var customerView = new CustomerOrderService(rig.Data, new FixedUser(new UserContext(rig.Customer.Id, rig.Organization, null, Role.Customer)), rig.Data);
        var order = await ReadyAssigned(rig);

        var ready = (await customerView.GetDetailsAsync(order.Id))!;
        Assert.Equal((null, null, null), (ready.DeliveryPartnerName, ready.DeliveryPartnerPhone, ready.DeliveryOtp));

        rig.SignInPartner(rig.Rina);
        await rig.Service.PickUpAsync(order.Id);
        var onTheWay = (await customerView.GetDetailsAsync(order.Id))!;
        Assert.Equal(("Rina", "9000000001", order.DeliveryOtp), (onTheWay.DeliveryPartnerName, onTheWay.DeliveryPartnerPhone, onTheWay.DeliveryOtp));
        Assert.Equal(order.DeliveryOtp, (await customerView.GetHistoryAsync())!.Single(item => item.Id == order.Id).DeliveryOtp);

        await rig.Service.DeliverAsync(order.Id, new DeliverRequest(order.DeliveryOtp!, 250));
        Assert.Null((await customerView.GetDetailsAsync(order.Id))!.DeliveryOtp);
    }

    [Fact]
    public async Task The_code_is_never_part_of_what_staff_or_other_customers_get()
    {
        var rig = Create();
        var order = await OutForDelivery(rig);

        rig.SignInStaff(rig.A);
        var assigned = await rig.Service.AssignAsync(order.Id, new AssignDeliveryRequest(rig.Rina.Id));
        Assert.Null(assigned.Value!.DeliveryOtp);

        var summaries = await rig.Data.GetScopedOrderSummariesAsync(rig.Organization, new HashSet<Guid> { rig.A.Id }, false);
        var row = summaries.Single(item => item.Id == order.Id);
        Assert.Equal(("Rina Patil", rig.Rina.Id), (row.DeliveryPartner, row.DeliveryPartnerId));

        var stranger = new CustomerOrderService(rig.Data, new FixedUser(new UserContext(Guid.NewGuid(), rig.Organization, null, Role.Customer)), rig.Data);
        Assert.Null(await stranger.GetDetailsAsync(order.Id));
    }

    [Fact]
    public void The_routes_answer_with_the_right_codes()
    {
        var controller = new DeliveryController(new CannedDelivery());
        static int Code(IActionResult result) => result switch { ObjectResult objectResult => objectResult.StatusCode ?? 200, StatusCodeResult statusResult => statusResult.StatusCode, _ => 0 };

        Assert.Equal(200, Code(controller.Mine(CancellationToken.None).Result));
        CannedDelivery.Next = DeliveryResultStatus.Invalid;
        Assert.Equal(400, Code(controller.Mine(CancellationToken.None).Result));
        CannedDelivery.Next = DeliveryResultStatus.WrongOtp;
        Assert.Equal(400, Code(controller.Mine(CancellationToken.None).Result));
        CannedDelivery.Next = DeliveryResultStatus.Locked;
        Assert.Equal(429, Code(controller.Mine(CancellationToken.None).Result));
        CannedDelivery.Next = DeliveryResultStatus.NotFound;
        Assert.Equal(404, Code(controller.Mine(CancellationToken.None).Result));
        CannedDelivery.Next = DeliveryResultStatus.WrongState;
        Assert.Equal(409, Code(controller.Mine(CancellationToken.None).Result));
        CannedDelivery.Next = DeliveryResultStatus.Succeeded;
    }

    private sealed class CannedDelivery : IDeliveryService
    {
        public static DeliveryResultStatus Next { get; set; } = DeliveryResultStatus.Succeeded;

        private static DeliveryResult<T> Answer<T>(T value) => Next == DeliveryResultStatus.Succeeded ? DeliveryResult<T>.Ok(value) : DeliveryResult<T>.Fail(Next, "no");

        public Task<DeliveryResult<IReadOnlyList<DeliveryPartnerResponse>>> ListPartnersAsync(Guid? storeId, CancellationToken cancellationToken = default) => Task.FromResult(Answer<IReadOnlyList<DeliveryPartnerResponse>>([]));
        public Task<DeliveryResult<OrderResponse>> AssignAsync(Guid orderId, AssignDeliveryRequest request, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<DeliveryResult<IReadOnlyList<PartnerDeliveryResponse>>> MineAsync(CancellationToken cancellationToken = default) => Task.FromResult(Answer<IReadOnlyList<PartnerDeliveryResponse>>([]));
        public Task<DeliveryResult<PartnerDeliveryResponse>> PickUpAsync(Guid orderId, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<DeliveryResult<PartnerDeliveryResponse>> DeliverAsync(Guid orderId, DeliverRequest request, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }
}

[Collection("SqlServer")]
public sealed class DeliveryEfIntegrationTests
{
    private static string? ConnectionString => CheckoutEfIntegrationTests.ConnectionString;

    private static async Task<(User Partner, Order Order)> SeedAsync(CheckoutEfIntegrationTests.World world, string method = PaymentMethods.CashOnDelivery, decimal total = 120)
    {
        await using var db = world.Db();
        var organizationId = (await db.Stores.AsNoTracking().FirstAsync(store => store.Id == world.Store.Id)).OrganizationId;
        var partner = new User { ExternalSubject = world.Token + "-partner@example.test", DisplayName = "Rina Patil", FirstName = "Rina", PhoneNumber = "9000000001", OrganizationId = organizationId, StoreId = world.Store.Id, Role = Role.DeliveryPartner };
        db.Users.Add(partner);
        var customerUserId = await db.Users.Where(user => user.ExternalSubject == world.Emails[0]).Select(user => user.Id).SingleAsync();
        var order = new Order { OrderNumber = "D-" + world.Token, UserId = customerUserId, StoreId = world.Store.Id, TotalAmount = total, PaymentMethod = method, Status = OrderStatus.Ready, DeliveryAddress = "Flat 4" };
        order.StatusHistory.Add(new OrderStatusHistory { Status = OrderStatus.Ready });
        db.Orders.Add(order);
        await db.SaveChangesAsync();
        return (partner, order);
    }

    private static async Task CleanAsync(CheckoutEfIntegrationTests.World world, User partner)
    {
        await using var db = world.Db();
        var orderIds = await db.Orders.Where(order => order.StoreId == world.Store.Id).Select(order => order.Id).ToListAsync();
        await db.Notifications.Where(note => note.OrderId != null && orderIds.Contains(note.OrderId.Value)).ExecuteDeleteAsync();
        await db.Orders.Where(order => order.StoreId == world.Store.Id).ExecuteDeleteAsync();
        await db.Users.Where(user => user.Id == partner.Id).ExecuteDeleteAsync();
    }

    private static async Task<DeliveryChange> AssignAsync(CheckoutEfIntegrationTests.World world, Order order, User partner)
    {
        await using var db = world.Db();
        return await new EfDeliveryStore(db).AssignAsync(order.Id, partner.OrganizationId, world.Store.Id, partner.Id, "4821", DateTime.UtcNow);
    }

    [Fact]
    public async Task A_delivery_goes_from_ready_to_delivered_with_its_code_cash_and_one_notification()
    {
        if (ConnectionString is null)
        {
            return;
        }

        await using var world = await CheckoutEfIntegrationTests.World.CreateAsync(customers: 1, stock: 5);
        var (partner, order) = await SeedAsync(world);
        try
        {
            Assert.Equal(DeliveryChangeStatus.Succeeded, (await AssignAsync(world, order, partner)).Status);
            await using (var db = world.Db())
            {
                var list = await new EfDeliveryStore(db).ListForPartnerAsync(partner.Id, DateTime.UtcNow.AddDays(-1));
                Assert.Equal(order.Id, list.Single().Order.Id);
                Assert.Equal(world.Store.Id, list.Single().Store.Id);
                Assert.Equal(1, (await new EfDeliveryStore(db).CountActiveAsync([partner.Id]))[partner.Id]);
            }

            await using (var db = world.Db())
            {
                Assert.Equal(DeliveryChangeStatus.Succeeded, (await new EfDeliveryStore(db).PickUpAsync(order.Id, partner.Id, DateTime.UtcNow)).Status);
            }

            await using (var db = world.Db())
            {
                Assert.Equal(DeliveryChangeStatus.WrongCash, (await new EfDeliveryStore(db).DeliverAsync(order.Id, partner.Id, "4821", 50, DateTime.UtcNow)).Status);
            }

            await using (var db = world.Db())
            {
                Assert.Equal(DeliveryChangeStatus.Succeeded, (await new EfDeliveryStore(db).DeliverAsync(order.Id, partner.Id, "4821", 120, DateTime.UtcNow)).Status);
            }

            await using var verify = world.Db();
            var saved = await verify.Orders.AsNoTracking().Include(item => item.StatusHistory).SingleAsync(item => item.Id == order.Id);
            Assert.Equal((OrderStatus.Delivered, 120m, partner.Id), (saved.Status, saved.CashCollected, saved.DeliveryPartnerId));
            Assert.Equal([OrderStatus.Ready, OrderStatus.OutForDelivery, OrderStatus.Delivered], saved.StatusHistory.OrderBy(item => item.ChangedAt).Select(item => item.Status));
            var types = await verify.Notifications.AsNoTracking().Where(note => note.OrderId == order.Id).Select(note => note.Type).ToListAsync();
            Assert.Equal(new[] { NotificationTypes.OrderDelivered }, types); // only the delivery rings the bell; pickup updates the timeline
        }
        finally
        {
            await CleanAsync(world, partner);
        }
    }

    [Fact]
    public async Task Wrong_codes_are_counted_in_the_database_and_five_lock_the_order()
    {
        if (ConnectionString is null)
        {
            return;
        }

        await using var world = await CheckoutEfIntegrationTests.World.CreateAsync(customers: 1, stock: 5);
        var (partner, order) = await SeedAsync(world);
        try
        {
            await AssignAsync(world, order, partner);
            await using (var db = world.Db())
            {
                await new EfDeliveryStore(db).PickUpAsync(order.Id, partner.Id, DateTime.UtcNow);
            }

            var results = new List<DeliveryChangeStatus>();
            for (var attempt = 1; attempt <= 6; attempt++)
            {
                await using var db = world.Db();
                results.Add((await new EfDeliveryStore(db).DeliverAsync(order.Id, partner.Id, "0000", 120, DateTime.UtcNow)).Status);
            }

            Assert.Equal([DeliveryChangeStatus.WrongOtp, DeliveryChangeStatus.WrongOtp, DeliveryChangeStatus.WrongOtp, DeliveryChangeStatus.WrongOtp, DeliveryChangeStatus.Locked, DeliveryChangeStatus.Locked], results);
            await using var verify = world.Db();
            var saved = await verify.Orders.AsNoTracking().SingleAsync(item => item.Id == order.Id);
            Assert.Equal(OrderStatus.OutForDelivery, saved.Status);
            Assert.NotNull(saved.OtpLockedUntil);
        }
        finally
        {
            await CleanAsync(world, partner);
        }
    }

    [Fact]
    public async Task Picking_up_and_delivering_twice_at_once_write_one_history_row_and_one_notification_each()
    {
        if (ConnectionString is null)
        {
            return;
        }

        await using var world = await CheckoutEfIntegrationTests.World.CreateAsync(customers: 1, stock: 5);
        var (partner, order) = await SeedAsync(world);
        try
        {
            await AssignAsync(world, order, partner);
            var pickups = await Task.WhenAll(Enumerable.Range(0, 4).Select(async _ =>
            {
                await using var db = world.Db();
                return (await new EfDeliveryStore(db).PickUpAsync(order.Id, partner.Id, DateTime.UtcNow)).Status;
            }));
            Assert.All(pickups, status => Assert.True(status is DeliveryChangeStatus.Succeeded or DeliveryChangeStatus.Unchanged, status.ToString()));

            var deliveries = await Task.WhenAll(Enumerable.Range(0, 4).Select(async _ =>
            {
                await using var db = world.Db();
                return (await new EfDeliveryStore(db).DeliverAsync(order.Id, partner.Id, "4821", 120, DateTime.UtcNow)).Status;
            }));
            Assert.All(deliveries, status => Assert.True(status is DeliveryChangeStatus.Succeeded or DeliveryChangeStatus.Unchanged, status.ToString()));

            await using var verify = world.Db();
            var saved = await verify.Orders.AsNoTracking().Include(item => item.StatusHistory).SingleAsync(item => item.Id == order.Id);
            Assert.Equal(1, saved.StatusHistory.Count(item => item.Status == OrderStatus.OutForDelivery));
            Assert.Equal(1, saved.StatusHistory.Count(item => item.Status == OrderStatus.Delivered));
            Assert.Equal(1, await verify.Notifications.CountAsync(note => note.OrderId == order.Id)); // the delivery, once
        }
        finally
        {
            await CleanAsync(world, partner);
        }
    }

    [Fact]
    public async Task Only_a_partner_of_the_orders_store_can_be_assigned_and_staff_of_another_store_cannot_assign()
    {
        if (ConnectionString is null)
        {
            return;
        }

        await using var world = await CheckoutEfIntegrationTests.World.CreateAsync(customers: 1, stock: 5);
        var (partner, order) = await SeedAsync(world);
        try
        {
            await using (var db = world.Db())
            {
                var customerUser = await db.Users.AsNoTracking().SingleAsync(user => user.ExternalSubject == world.Emails[0]);
                Assert.Equal(DeliveryChangeStatus.PartnerInvalid, (await new EfDeliveryStore(db).AssignAsync(order.Id, partner.OrganizationId, world.Store.Id, customerUser.Id, "1111", DateTime.UtcNow)).Status);
                Assert.Equal(DeliveryChangeStatus.NotFound, (await new EfDeliveryStore(db).AssignAsync(order.Id, partner.OrganizationId, Guid.NewGuid(), partner.Id, "1111", DateTime.UtcNow)).Status);
                Assert.Equal(DeliveryChangeStatus.Succeeded, (await new EfDeliveryStore(db).AssignAsync(order.Id, partner.OrganizationId, null, partner.Id, "1111", DateTime.UtcNow)).Status);
            }

            await using var verify = world.Db();
            var summaries = await new EfCommerceStore(verify).GetScopedOrderSummariesAsync(partner.OrganizationId, new HashSet<Guid> { world.Store.Id }, false);
            Assert.Equal("Rina Patil", summaries.Single(item => item.Id == order.Id).DeliveryPartner);
            Assert.Equal(OrderLifecycleStatus.InvalidTransition, (await new EfCommerceStore(verify).TryTransitionOrderAsync(order.Id, partner.OrganizationId, world.Store.Id, OrderStatus.Completed)).Status);
        }
        finally
        {
            await CleanAsync(world, partner);
        }
    }
}
