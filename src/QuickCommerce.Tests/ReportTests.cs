using Microsoft.EntityFrameworkCore;
using QuickCommerce.Application.DTOs;
using QuickCommerce.Application.Interfaces;
using QuickCommerce.Application.Services;
using QuickCommerce.Domain;
using QuickCommerce.Infrastructure.Persistence;
using Xunit;

namespace QuickCommerce.Tests;

/// <summary>Phase P17: sales and stock reports. Revenue is orders that reached the customer; days are India days.</summary>
public sealed class ReportTests
{
    private static readonly Guid StoreA = Guid.Parse("a0000000-0000-0000-0000-000000000001");
    private static readonly Guid StoreB = Guid.Parse("b0000000-0000-0000-0000-000000000002");
    private static readonly Guid Org = Guid.NewGuid();
    private static readonly DateOnly Day = new(2026, 10, 14);

    // 14 October 2026, 10:00 India time is 04:30 UTC.
    private static DateTime At(int day, int hour, int minute = 0) => new DateTime(2026, 10, day, hour, minute, 0, DateTimeKind.Utc);

    private static ReportOrderRow Row(string number, OrderStatus status, decimal total, DateTime? at = null, Guid? store = null, string method = PaymentMethods.CashOnDelivery, PaymentState payment = PaymentState.NotRequired, Guid? user = null) =>
        new(Guid.NewGuid(), number, store ?? StoreA, user ?? Guid.NewGuid(), at ?? At(14, 4, 30), status, method, payment, total - 30, 25, 5, total);

    // ---------------- the days ----------------

    [Fact]
    public void An_india_day_starts_at_half_past_six_the_evening_before_in_utc()
    {
        Assert.Equal(new DateTime(2026, 10, 13, 18, 30, 0, DateTimeKind.Utc), ReportMath.StartUtc(Day));
        Assert.Equal(new DateTime(2026, 10, 14, 18, 30, 0, DateTimeKind.Utc), ReportMath.EndUtc(Day));
        Assert.Equal(new DateTime(2026, 10, 14, 10, 0, 0), ReportMath.ToIndia(At(14, 4, 30)));
    }

    [Fact]
    public void An_order_a_second_before_india_midnight_belongs_to_the_old_day_and_one_at_midnight_to_the_new()
    {
        var rows = new[]
        {
            Row("LATE", OrderStatus.Delivered, 100, new DateTime(2026, 10, 14, 18, 29, 59, DateTimeKind.Utc)),
            Row("NEXT", OrderStatus.Delivered, 200, new DateTime(2026, 10, 14, 18, 30, 0, DateTimeKind.Utc))
        };

        var days = ReportMath.Days(rows, Day, Day.AddDays(1));

        Assert.Equal((1, 100m), (days[0].Orders, days[0].Revenue));
        Assert.Equal((1, 200m), (days[1].Orders, days[1].Revenue));
    }

    [Fact]
    public void Every_day_of_the_range_is_listed_even_when_nothing_was_ordered()
    {
        var days = ReportMath.Days([Row("A", OrderStatus.Delivered, 100, At(16, 5))], Day, Day.AddDays(4));

        Assert.Equal(5, days.Count);
        Assert.Equal([Day, Day.AddDays(1), Day.AddDays(2), Day.AddDays(3), Day.AddDays(4)], days.Select(day => day.Date));
        Assert.Equal([0, 0, 1, 0, 0], days.Select(day => day.Orders));
        Assert.Equal(0m, days[0].Revenue);
    }

    [Theory]
    [InlineData(2026, 10, 14, 2026, 10, 14, null)]
    [InlineData(2026, 10, 1, 2026, 12, 31, null)]
    [InlineData(2026, 10, 14, 2026, 10, 13, "The end date is before the start date.")]
    [InlineData(2026, 1, 1, 2026, 4, 3, "Choose at most 92 days.")]
    public void The_range_must_run_forwards_and_be_at_most_ninety_two_days(int fy, int fm, int fd, int ty, int tm, int td, string? problem) =>
        Assert.Equal(problem, ReportMath.Check(new DateOnly(fy, fm, fd), new DateOnly(ty, tm, td)));

    [Fact]
    public void Ninety_two_days_is_allowed_and_ninety_three_is_not()
    {
        Assert.Null(ReportMath.Check(Day, Day.AddDays(91)));
        Assert.NotNull(ReportMath.Check(Day, Day.AddDays(92)));
    }

    [Fact]
    public void Orders_are_counted_by_the_india_hour_they_were_placed()
    {
        var hours = ReportMath.Hours([Row("A", OrderStatus.Delivered, 1, At(14, 4, 30)), Row("B", OrderStatus.Cancelled, 1, At(14, 4, 59)), Row("C", OrderStatus.Delivered, 1, At(14, 18, 30))]);

        Assert.Equal(24, hours.Count);
        Assert.Equal(2, hours.Single(hour => hour.Hour == 10).Orders);
        Assert.Equal(1, hours.Single(hour => hour.Hour == 0).Orders);
        Assert.Equal(0, hours.Single(hour => hour.Hour == 3).Orders);
    }

    // ---------------- the totals ----------------

    [Fact]
    public void Revenue_is_only_orders_that_reached_the_customer_and_every_other_state_is_counted_apart()
    {
        var rows = new[]
        {
            Row("D1", OrderStatus.Delivered, 100),
            Row("D2", OrderStatus.Completed, 300, method: PaymentMethods.Online, payment: PaymentState.Paid),
            Row("P1", OrderStatus.Pending, 50),
            Row("P2", OrderStatus.Accepted, 50),
            Row("P3", OrderStatus.Preparing, 50),
            Row("P4", OrderStatus.Ready, 50),
            Row("P5", OrderStatus.OutForDelivery, 50),
            Row("C1", OrderStatus.Cancelled, 999),
            Row("R1", OrderStatus.Rejected, 999)
        };

        var totals = ReportMath.Totals(rows, newCustomers: 4);

        Assert.Equal((9, 2, 5, 1, 1), (totals.Orders, totals.Delivered, totals.InProgress, totals.Cancelled, totals.Rejected));
        Assert.Equal((400m, 200m, 100m, 300m, 4), (totals.Revenue, totals.AverageOrder, totals.CashRevenue, totals.OnlineRevenue, totals.NewCustomers));
    }

    [Fact]
    public void With_nothing_delivered_revenue_and_average_are_zero_not_an_error()
    {
        var totals = ReportMath.Totals([Row("P", OrderStatus.Pending, 80)], 0);
        Assert.Equal((0m, 0m, 1, 0), (totals.Revenue, totals.AverageOrder, totals.Orders, totals.Delivered));
        var empty = ReportMath.Totals([], 0);
        Assert.Equal((0, 0m, 0m), (empty.Orders, empty.Revenue, empty.AverageOrder));
    }

    [Fact]
    public void The_average_is_rounded_to_paise()
    {
        var totals = ReportMath.Totals([Row("A", OrderStatus.Delivered, 100), Row("B", OrderStatus.Delivered, 100), Row("C", OrderStatus.Delivered, 101)], 0);
        Assert.Equal(100.33m, totals.AverageOrder);
    }

    [Fact]
    public void Refunds_are_the_online_orders_whose_money_went_back_or_is_going_back()
    {
        var totals = ReportMath.Totals(
        [
            Row("R1", OrderStatus.Cancelled, 120, method: PaymentMethods.Online, payment: PaymentState.Refunded),
            Row("R2", OrderStatus.Rejected, 80, method: PaymentMethods.Online, payment: PaymentState.Refunding),
            Row("PAID", OrderStatus.Delivered, 500, method: PaymentMethods.Online, payment: PaymentState.Paid),
            Row("FAIL", OrderStatus.Cancelled, 70, method: PaymentMethods.Online, payment: PaymentState.RefundFailed)
        ], 0);

        Assert.Equal((2, 200m), (totals.Refunds, totals.RefundedAmount));
    }

    [Fact]
    public void Stores_are_ranked_by_revenue_and_a_store_with_no_orders_is_still_listed()
    {
        var third = Guid.NewGuid();
        var names = new Dictionary<Guid, string> { [StoreA] = "Kharghar", [StoreB] = "Panvel", [third] = "Belapur" };

        var stores = ReportMath.Stores([Row("A", OrderStatus.Delivered, 100, store: StoreA), Row("B", OrderStatus.Delivered, 900, store: StoreB), Row("B2", OrderStatus.Pending, 50, store: StoreB)], names);

        Assert.Equal(["Panvel", "Kharghar", "Belapur"], stores.Select(row => row.Store));
        Assert.Equal((2, 1, 900m), (stores[0].Orders, stores[0].Delivered, stores[0].Revenue));
        Assert.Equal((0, 0m), (stores[2].Orders, stores[2].Revenue));
    }

    // ---------------- the csv ----------------

    [Theory]
    [InlineData("KHG-1", "KHG-1")]
    [InlineData("=SUM(A1)", "'=SUM(A1)")]
    [InlineData("+91", "'+91")]
    [InlineData("-5", "'-5")]
    [InlineData("@x", "'@x")]
    [InlineData("a,b", "\"a,b\"")]
    [InlineData("say \"hi\"", "\"say \"\"hi\"\"\"")]
    [InlineData("", "")]
    public void A_csv_cell_cannot_run_as_a_spreadsheet_formula_and_is_quoted_when_needed(string value, string expected) => Assert.Equal(expected, ReportMath.Csv(value));

    [Fact]
    public void The_csv_lists_orders_oldest_first_in_india_time_with_money_and_nothing_that_identifies_a_customer()
    {
        var user = Guid.NewGuid();
        var rows = new[]
        {
            Row("KHG-2", OrderStatus.Delivered, 150.5m, At(14, 7, 0), user: user),
            Row("=EVIL()", OrderStatus.Cancelled, 80, At(14, 4, 30))
        };

        var csv = ReportMath.OrdersCsv(rows, new Dictionary<Guid, string> { [StoreA] = "Kharghar, Navi Mumbai" });
        var lines = csv.Split("\r\n", StringSplitOptions.RemoveEmptyEntries);

        Assert.Equal("Order,Placed (India time),Store,Status,Payment method,Payment status,Item total,Delivery fee,Handling fee,Total", lines[0]);
        Assert.Equal("'=EVIL(),2026-10-14 10:00,\"Kharghar, Navi Mumbai\",Cancelled,CashOnDelivery,NotRequired,50.00,25.00,5.00,80.00", lines[1]);
        Assert.Equal("KHG-2,2026-10-14 12:30,\"Kharghar, Navi Mumbai\",Delivered,CashOnDelivery,NotRequired,120.50,25.00,5.00,150.50", lines[2]);
        Assert.DoesNotContain(user.ToString(), csv);
    }

    // ---------------- who may see what ----------------

    private sealed class FixedUser(UserContext? context) : ICurrentUserContextResolver
    {
        public UserContext? Context { get; set; } = context;
        public Task<UserContext?> ResolveAsync(CancellationToken cancellationToken = default) => Task.FromResult(Context);
    }

    private sealed class FakeStore : IReportStore
    {
        public List<ReportOrderRow> Rows { get; } = [];
        public Dictionary<Guid, string> Stores { get; } = new() { [StoreA] = "Kharghar", [StoreB] = "Panvel" };
        public IReadOnlyCollection<Guid>? LastScope { get; private set; } = [Guid.Empty];
        public (DateTime From, DateTime To) LastWindow { get; private set; }
        public int IncomeCalls { get; private set; }

        public Task<IReadOnlyList<ReportOrderRow>> GetOrderRowsAsync(Guid organizationId, IReadOnlyCollection<Guid>? storeIds, DateTime fromUtc, DateTime toUtc, CancellationToken cancellationToken = default)
        {
            LastScope = storeIds;
            LastWindow = (fromUtc, toUtc);
            return Task.FromResult<IReadOnlyList<ReportOrderRow>>(Rows.Where(row => (storeIds is null || storeIds.Contains(row.StoreId)) && row.CreatedAt >= fromUtc && row.CreatedAt < toUtc).ToArray());
        }

        public Task<IReadOnlyList<ReportProduct>> GetTopProductsAsync(Guid organizationId, IReadOnlyCollection<Guid>? storeIds, DateTime fromUtc, DateTime toUtc, int take, CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<ReportProduct>>([new ReportProduct(Guid.NewGuid(), "Milk", 3, 102)]);

        public Task<int> CountNewCustomersAsync(Guid organizationId, IReadOnlyCollection<Guid>? storeIds, DateTime fromUtc, DateTime toUtc, CancellationToken cancellationToken = default) => Task.FromResult(2);

        public Task<IReadOnlyList<LowStockRow>> GetLowStockAsync(Guid organizationId, IReadOnlyCollection<Guid>? storeIds, int take, CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<LowStockRow>>([new LowStockRow(StoreA, "Kharghar", "Milk", "500 ml", 1, 5)]);

        public Task<PlatformIncome> GetIncomeAsync(Guid organizationId, DateTime fromUtc, DateTime toUtc, CancellationToken cancellationToken = default)
        {
            IncomeCalls++;
            return Task.FromResult(new PlatformIncome(10, 0, 500, 100));
        }

        public Task<IReadOnlyDictionary<Guid, string>> GetStoreNamesAsync(Guid organizationId, IReadOnlyCollection<Guid>? storeIds, CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyDictionary<Guid, string>>(Stores.Where(store => storeIds is null || storeIds.Contains(store.Key)).ToDictionary(store => store.Key, store => store.Value));
    }

    private static (ReportService Service, FakeStore Store, FixedUser Who) Create(Role role, Guid? store = null)
    {
        var data = new FakeStore();
        data.Rows.Add(Row("A1", OrderStatus.Delivered, 100, store: StoreA));
        data.Rows.Add(Row("B1", OrderStatus.Delivered, 700, store: StoreB));
        data.Rows.Add(Row("HOLD", OrderStatus.AwaitingPayment, 999, store: StoreA));
        var who = new FixedUser(new UserContext(Guid.NewGuid(), Org, store, role));
        return (new ReportService(data, who), data, who);
    }

    [Fact]
    public async Task Store_staff_get_their_own_store_even_when_they_ask_for_another()
    {
        var (service, data, _) = Create(Role.StoreStaff, StoreA);

        var report = (await service.GetAsync(Day, Day, StoreB)).Value!;

        Assert.Equal([StoreA], data.LastScope);
        Assert.Equal((1, 100m), (report.Totals.Orders, report.Totals.Revenue));
        Assert.Equal((StoreA, "Kharghar"), (report.StoreId, report.StoreName));
        Assert.Empty(report.Stores);
        Assert.Null(report.Income);
        Assert.Equal(0, data.IncomeCalls);
    }

    [Fact]
    public async Task An_administrator_without_a_store_sees_every_store_the_by_store_table_and_the_platform_income()
    {
        var (service, data, _) = Create(Role.Admin);

        var report = (await service.GetAsync(Day, Day, null)).Value!;

        Assert.Null(data.LastScope);
        Assert.Equal((2, 800m), (report.Totals.Orders, report.Totals.Revenue));
        Assert.Equal(["Panvel", "Kharghar"], report.Stores.Select(row => row.Store));
        Assert.Equal(new PlatformIncome(10, 0, 500, 100), report.Income);
        Assert.Equal((2, 1), (report.Totals.NewCustomers, report.LowStock.Count));
    }

    [Fact]
    public async Task An_administrator_can_narrow_to_one_store_and_then_sees_no_platform_income()
    {
        var (service, data, _) = Create(Role.ApplicationAdmin);

        var report = (await service.GetAsync(Day, Day, StoreB)).Value!;

        Assert.Equal([StoreB], data.LastScope);
        Assert.Equal(700m, report.Totals.Revenue);
        Assert.Equal("Panvel", report.StoreName);
        Assert.Null(report.Income);
    }

    [Fact]
    public async Task A_store_that_does_not_exist_is_not_found()
    {
        var (service, _, _) = Create(Role.Admin);

        Assert.Equal(ReportStatus.NotFound, (await service.GetAsync(Day, Day, Guid.NewGuid())).Status);
    }

    [Theory]
    [InlineData(Role.Customer)]
    [InlineData(Role.DeliveryPartner)]
    public async Task Customers_and_delivery_partners_have_no_reports(Role role)
    {
        var (service, data, _) = Create(role, StoreA);

        Assert.Equal(ReportStatus.Forbidden, (await service.GetAsync(Day, Day, null)).Status);
        Assert.Equal(ReportStatus.Forbidden, (await service.OrdersCsvAsync(Day, Day, null)).Status);
        Assert.Equal(0, data.IncomeCalls);
    }

    [Fact]
    public async Task Nobody_signed_in_and_staff_without_a_store_have_no_reports()
    {
        var (service, _, who) = Create(Role.StoreStaff, null);
        Assert.Equal(ReportStatus.Forbidden, (await service.GetAsync(Day, Day, null)).Status);
        who.Context = null;
        Assert.Equal(ReportStatus.Forbidden, (await service.GetAsync(Day, Day, null)).Status);
    }

    [Fact]
    public async Task The_window_asked_of_the_store_is_the_whole_india_days()
    {
        var (service, data, _) = Create(Role.Admin);

        await service.GetAsync(Day, Day.AddDays(1), null);

        Assert.Equal((new DateTime(2026, 10, 13, 18, 30, 0, DateTimeKind.Utc), new DateTime(2026, 10, 15, 18, 30, 0, DateTimeKind.Utc)), data.LastWindow);
    }

    [Fact]
    public async Task A_bad_range_is_refused_before_anything_is_read()
    {
        var (service, data, _) = Create(Role.Admin);

        Assert.Equal(ReportStatus.Invalid, (await service.GetAsync(Day, Day.AddDays(-1), null)).Status);
        Assert.Equal(ReportStatus.Invalid, (await service.GetAsync(Day, Day.AddDays(100), null)).Status);
        Assert.Equal(ReportStatus.Invalid, (await service.OrdersCsvAsync(Day, Day.AddDays(100), null)).Status);
        Assert.Equal(new[] { Guid.Empty }, data.LastScope);
    }

    [Fact]
    public async Task Unpaid_holds_are_in_no_count_and_not_in_the_csv()
    {
        var (service, _, _) = Create(Role.StoreStaff, StoreA);

        var report = (await service.GetAsync(Day, Day, null)).Value!;
        var csv = (await service.OrdersCsvAsync(Day, Day, null)).Value!;

        Assert.Equal(1, report.Totals.Orders);
        Assert.Contains("A1", csv);
        Assert.DoesNotContain("HOLD", csv);
        Assert.DoesNotContain("B1", csv);
    }
}

[Collection("SqlServer")]
public sealed class ReportEfIntegrationTests
{
    private static string? ConnectionString => CheckoutEfIntegrationTests.ConnectionString;

    private static readonly DateTime From = new(2026, 10, 13, 18, 30, 0, DateTimeKind.Utc);
    private static readonly DateTime To = From.AddDays(1);

    private static async Task<Guid> UserAsync(CheckoutEfIntegrationTests.World world, int index)
    {
        await using var db = world.Db();
        return await db.Users.Where(user => user.ExternalSubject == world.Emails[index]).Select(user => user.Id).SingleAsync();
    }

    private static async Task<Guid> OrderAsync(CheckoutEfIntegrationTests.World world, Guid store, Guid user, OrderStatus status, decimal total, DateTime at, string method = PaymentMethods.CashOnDelivery, PaymentState payment = PaymentState.NotRequired, Guid? product = null, int quantity = 1, decimal unit = 0)
    {
        await using var db = world.Db();
        var order = new Order { OrderNumber = "RP-" + Guid.NewGuid().ToString("N")[..10], UserId = user, StoreId = store, TotalAmount = total, SubtotalAmount = total, Status = status, PaymentMethod = method, PaymentStatus = payment, DeliveryAddress = "x", CreatedAt = at };
        if (product is not null)
        {
            order.Items.Add(new OrderItem { ProductId = product.Value, ProductNameSnapshot = "Tomato", UnitPrice = unit, Quantity = quantity });
        }

        db.Orders.Add(order);
        await db.SaveChangesAsync();
        return order.Id;
    }

    [Fact]
    public async Task Orders_are_read_for_the_window_the_stores_asked_for_and_the_organization()
    {
        if (ConnectionString is null)
        {
            return;
        }

        await using var world = await CheckoutEfIntegrationTests.World.CreateAsync(customers: 1, stock: 5);
        var user = await UserAsync(world, 0);
        var otherStore = new Store { Name = "Other " + Guid.NewGuid().ToString("N")[..6], Address = "x", OrganizationId = world.Store.OrganizationId };
        try
        {
            await using (var db = world.Db())
            {
                db.Stores.Add(otherStore);
                await db.SaveChangesAsync();
            }

            await OrderAsync(world, world.Store.Id, user, OrderStatus.Delivered, 100, From.AddHours(1));
            await OrderAsync(world, world.Store.Id, user, OrderStatus.Pending, 50, From);
            await OrderAsync(world, world.Store.Id, user, OrderStatus.Delivered, 70, To);
            await OrderAsync(world, world.Store.Id, user, OrderStatus.Delivered, 60, From.AddSeconds(-1));
            await OrderAsync(world, otherStore.Id, user, OrderStatus.Delivered, 900, From.AddHours(2));

            await using var db2 = world.Db();
            var store = new EfReportStore(db2);
            var mine = await store.GetOrderRowsAsync(world.Store.OrganizationId, [world.Store.Id], From, To);
            Assert.Equal([100m, 50m], mine.OrderBy(row => row.CreatedAt).Reverse().Select(row => row.Total));
            var both = await store.GetOrderRowsAsync(world.Store.OrganizationId, null, From, To);
            Assert.Equal(3, both.Count(row => row.Total is 100 or 50 or 900));
            Assert.Empty(await store.GetOrderRowsAsync(Guid.NewGuid(), null, From, To));
            Assert.Equal([world.Store.Name], (await store.GetStoreNamesAsync(world.Store.OrganizationId, [world.Store.Id])).Values);
            Assert.Empty(await store.GetStoreNamesAsync(Guid.NewGuid(), [world.Store.Id]));
        }
        finally
        {
            await using var db = world.Db();
            await db.Orders.Where(order => order.StoreId == otherStore.Id).ExecuteDeleteAsync();
            await db.Stores.Where(store => store.Id == otherStore.Id).ExecuteDeleteAsync();
        }
    }

    [Fact]
    public async Task Top_products_add_up_quantity_and_revenue_of_delivered_orders_only()
    {
        if (ConnectionString is null)
        {
            return;
        }

        await using var world = await CheckoutEfIntegrationTests.World.CreateAsync(customers: 1, stock: 5);
        var user = await UserAsync(world, 0);
        await OrderAsync(world, world.Store.Id, user, OrderStatus.Delivered, 90, From.AddHours(1), product: world.Product.Id, quantity: 3, unit: 30);
        await OrderAsync(world, world.Store.Id, user, OrderStatus.Completed, 60, From.AddHours(2), product: world.Product.Id, quantity: 2, unit: 30);
        await OrderAsync(world, world.Store.Id, user, OrderStatus.Cancelled, 300, From.AddHours(3), product: world.Product.Id, quantity: 10, unit: 30);
        await OrderAsync(world, world.Store.Id, user, OrderStatus.Delivered, 30, To.AddHours(1), product: world.Product.Id, quantity: 1, unit: 30);

        await using var db = world.Db();
        var top = await new EfReportStore(db).GetTopProductsAsync(world.Store.OrganizationId, [world.Store.Id], From, To, 10);

        var row = Assert.Single(top);
        Assert.Equal((world.Product.Id, 5, 150m), (row.ProductId, row.Quantity, row.Revenue));
    }

    [Fact]
    public async Task A_customer_is_new_in_the_window_of_their_first_order_from_those_stores_and_unpaid_holds_do_not_count()
    {
        if (ConnectionString is null)
        {
            return;
        }

        await using var world = await CheckoutEfIntegrationTests.World.CreateAsync(customers: 3, stock: 5);
        var returning = await UserAsync(world, 0);
        var fresh = await UserAsync(world, 1);
        var onlyHold = await UserAsync(world, 2);
        await OrderAsync(world, world.Store.Id, returning, OrderStatus.Delivered, 10, From.AddDays(-5));
        await OrderAsync(world, world.Store.Id, returning, OrderStatus.Delivered, 10, From.AddHours(1));
        await OrderAsync(world, world.Store.Id, fresh, OrderStatus.Delivered, 10, From.AddHours(2));
        await OrderAsync(world, world.Store.Id, fresh, OrderStatus.Delivered, 10, From.AddHours(3));
        await OrderAsync(world, world.Store.Id, onlyHold, OrderStatus.AwaitingPayment, 10, From.AddHours(4));

        await using var db = world.Db();
        Assert.Equal(1, await new EfReportStore(db).CountNewCustomersAsync(world.Store.OrganizationId, [world.Store.Id], From, To));
    }

    [Fact]
    public async Task Low_stock_lists_active_sizes_at_or_below_their_reorder_level_for_the_stores_asked_for()
    {
        if (ConnectionString is null)
        {
            return;
        }

        await using var world = await CheckoutEfIntegrationTests.World.CreateAsync(customers: 1, stock: 2);
        await using (var db = world.Db())
        {
            await db.StoreVariantInventory.Where(row => row.StoreId == world.Store.Id && row.VariantId == world.Kilo.Id).ExecuteUpdateAsync(set => set.SetProperty(row => row.ReorderThreshold, 5));
        }

        await using var read = world.Db();
        var store = new EfReportStore(read);
        var low = await store.GetLowStockAsync(world.Store.OrganizationId, [world.Store.Id], 50);
        Assert.Equal((world.Product.Name, "1 kg", 2, 5), (low.Single().Product, low.Single().Size, low.Single().Available, low.Single().ReorderThreshold));

        await using (var db = world.Db())
        {
            await db.StoreVariantInventory.Where(row => row.StoreId == world.Store.Id && row.VariantId == world.Kilo.Id).ExecuteUpdateAsync(set => set.SetProperty(row => row.AvailableQuantity, 50));
        }

        await using var again = world.Db();
        Assert.Empty(await new EfReportStore(again).GetLowStockAsync(world.Store.OrganizationId, [world.Store.Id], 50));
    }

    [Fact]
    public async Task Platform_income_adds_up_paid_and_returned_offer_fees_and_bids_inside_the_window()
    {
        if (ConnectionString is null)
        {
            return;
        }

        await using var world = await CheckoutEfIntegrationTests.World.CreateAsync(customers: 1, stock: 5);
        var created = new List<Guid>();
        var pincode = "9" + Random.Shared.Next(10_000, 99_999);
        try
        {
            await using (var db = world.Db())
            {
                var from = new DateTime(2025, 1, 1, 0, 0, 0, DateTimeKind.Utc);
                var offers = new[]
                {
                    new Campaign { TitleEn = "t", BodyEn = "b", FeePaise = 10_000, PaymentStatus = PaymentState.Paid, CreatedAt = from.AddDays(1) },
                    new Campaign { TitleEn = "t", BodyEn = "b", FeePaise = 5_000, PaymentStatus = PaymentState.Refunded, CreatedAt = from.AddDays(1) },
                    new Campaign { TitleEn = "t", BodyEn = "b", FeePaise = 0, PaymentStatus = PaymentState.NotRequired, CreatedAt = from.AddDays(1) },
                    new Campaign { TitleEn = "t", BodyEn = "b", FeePaise = 99_000, PaymentStatus = PaymentState.Paid, CreatedAt = from.AddDays(40) }
                };
                created.AddRange(offers.Select(offer => offer.Id));
                db.Campaigns.AddRange(offers);
                db.PlacementBids.AddRange(
                    new PlacementBid { StoreId = world.Store.Id, Pincode = pincode, WeekStart = new DateOnly(2025, 1, 6), AmountPaise = 60_000, Status = PlacementBidStatus.Leading, PaymentStatus = PaymentState.Paid, CreatedAt = from.AddDays(2) },
                    new PlacementBid { StoreId = world.Store.Id, Pincode = pincode, WeekStart = new DateOnly(2025, 1, 13), AmountPaise = 40_000, Status = PlacementBidStatus.Outbid, PaymentStatus = PaymentState.Refunding, CreatedAt = from.AddDays(2) },
                    new PlacementBid { StoreId = world.Store.Id, Pincode = pincode, WeekStart = new DateOnly(2025, 1, 20), AmountPaise = 70_000, Status = PlacementBidStatus.AwaitingPayment, PaymentStatus = PaymentState.Created, CreatedAt = from.AddDays(2) });
                await db.SaveChangesAsync();
            }

            await using var read = world.Db();
            var income = await new EfReportStore(read).GetIncomeAsync(world.Store.OrganizationId, new DateTime(2025, 1, 1, 0, 0, 0, DateTimeKind.Utc), new DateTime(2025, 1, 31, 0, 0, 0, DateTimeKind.Utc));

            Assert.Equal(new PlatformIncome(100m, 50m, 600m, 400m), income);
        }
        finally
        {
            await using var db = world.Db();
            await db.PlacementBids.Where(bid => bid.Pincode == pincode).ExecuteDeleteAsync();
            await db.Campaigns.Where(offer => created.Contains(offer.Id)).ExecuteDeleteAsync();
        }
    }
}
