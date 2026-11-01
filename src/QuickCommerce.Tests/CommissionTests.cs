using System.Security.Claims;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using QuickCommerce.Api.Security;
using QuickCommerce.Application.DTOs;
using QuickCommerce.Application.Interfaces;
using QuickCommerce.Application.Services;
using QuickCommerce.Application.Validators;
using QuickCommerce.Domain;
using QuickCommerce.Infrastructure;
using QuickCommerce.Infrastructure.Persistence;
using QuickCommerce.Infrastructure.Security;
using Xunit;

namespace QuickCommerce.Tests;

/// <summary>P22 phase B: the platform's commission per store, fixed on each order, and the per-shop earnings in the report.</summary>
[Collection("SqlServer")]
public sealed class CommissionTests
{
    private sealed class FixedScope(bool admin, Guid organizationId) : IAuthorizationScopeService
    {
        private AuthorizationScope Scope => new(Guid.NewGuid(), organizationId, new HashSet<string> { admin ? "ApplicationAdmin" : "StoreStaff" }, new HashSet<Guid>(), new HashSet<string>());
        public Task<AuthorizationScope?> ResolveAsync(CancellationToken cancellationToken = default) => Task.FromResult<AuthorizationScope?>(Scope);
        public Task<AuthorizationScope?> ResolveForUserAsync(Guid userId, CancellationToken cancellationToken = default) => Task.FromResult<AuthorizationScope?>(Scope);
        public Task<bool> HasPermissionAsync(string permissionCode, CancellationToken cancellationToken = default) => Task.FromResult(admin);
        public Task<bool> CanAccessStoreAsync(Guid storeId, CancellationToken cancellationToken = default) => Task.FromResult(admin);
        public Task<bool> OwnsCustomerAsync(Guid customerId, CancellationToken cancellationToken = default) => Task.FromResult(false);
    }

    private sealed class Rig
    {
        public required InMemoryCommerceStore Data { get; init; }
        public required Store Harbor { get; init; }
        public required Store Other { get; init; }
        public required Product Tomato { get; init; }
        public required CommissionService Admin { get; init; }
        public required CommissionService Staff { get; init; }
        public required CartService Cart { get; init; }
        public required CheckoutService Checkout { get; init; }
        public required CheckoutService CheckoutWithoutCommission { get; init; }

        public async Task<Order> Place(CheckoutService? checkout = null)
        {
            await Cart.AddItemAsync(Harbor.Id, new AddCartItemRequest(Tomato.Id, 2));
            var result = await (checkout ?? Checkout).CheckoutAsync(Harbor.Id, new CheckoutRequest("12 Main Street", 19.07, 72.87));
            Assert.Equal(CheckoutOperationStatus.Succeeded, result.Status);
            return Data.Orders.Last();
        }
    }

    private static Rig Create(decimal kiloPrice = 100)
    {
        var data = InMemoryCommerceStore.CreateSeeded();
        var harbor = data.Stores.Single(store => store.Name == "Harbor Point Dark Store");
        var other = data.Stores.First(store => store.Id != harbor.Id);
        var tomato = data.Products.Single(product => product.Name == "Tomato");
        var kilo = data.DefaultVariantOf(tomato);
        kilo.Price = kiloPrice;
        kilo.Mrp = null;
        var organization = data.Organizations.Single().Id;
        var user = data.Users.Single();
        var currentUser = new HttpContextCurrentUser(new HttpContextAccessor
        {
            HttpContext = new DefaultHttpContext
            {
                User = new ClaimsPrincipal(new ClaimsIdentity(
                [
                    new Claim(ClaimTypes.NameIdentifier, user.ExternalSubject),
                    new Claim("organization_id", organization.ToString())
                ], "Test"))
            }
        });
        var resolver = new CurrentUserContextResolver(currentUser, data);
        return new Rig
        {
            Data = data,
            Harbor = harbor,
            Other = other,
            Tomato = tomato,
            Admin = new CommissionService(data, new FixedScope(true, organization)),
            Staff = new CommissionService(data, new FixedScope(false, organization)),
            Cart = new CartService(data, resolver, new AddCartItemRequestValidator(), new UpdateCartItemRequestValidator(), null, data),
            Checkout = new CheckoutService(data, resolver, new CheckoutRequestValidator(), commissions: data),
            CheckoutWithoutCommission = new CheckoutService(data, resolver, new CheckoutRequestValidator())
        };
    }

    // ---------------- the setting ----------------

    [Theory]
    [InlineData(200, 15, 30)]
    [InlineData(33.33, 12.5, 4.17)]
    [InlineData(99.99, 10, 10)]
    [InlineData(100, 0, 0)]
    public void The_commission_is_a_percent_of_the_product_total_rounded_to_the_paisa(double total, double percent, double expected) =>
        Assert.Equal((decimal)expected, CommissionMath.Amount((decimal)total, (decimal)percent));

    [Theory]
    [InlineData(-1)]
    [InlineData(100.01)]
    [InlineData(12.345)]
    public async Task A_percent_outside_0_to_100_or_with_more_than_two_decimals_is_refused(double percent)
    {
        var rig = Create();

        Assert.Equal(CommissionStatus.Invalid, (await rig.Admin.SetDefaultAsync((decimal)percent)).Status);
        Assert.Equal(CommissionStatus.Invalid, (await rig.Admin.SetStoreAsync(rig.Harbor.Id, (decimal)percent)).Status);
        Assert.Empty(rig.Data.CommissionRates);
    }

    [Fact]
    public async Task Only_an_administrator_can_see_or_change_it()
    {
        var rig = Create();

        Assert.Equal(CommissionStatus.Forbidden, (await rig.Staff.GetAsync()).Status);
        Assert.Equal(CommissionStatus.Forbidden, (await rig.Staff.SetDefaultAsync(10)).Status);
        Assert.Equal(CommissionStatus.Forbidden, (await rig.Staff.SetStoreAsync(rig.Harbor.Id, 10)).Status);
    }

    [Fact]
    public async Task A_store_uses_its_own_percent_else_the_default_and_removing_its_own_goes_back_to_the_default()
    {
        var rig = Create();
        await rig.Admin.SetDefaultAsync(10);
        await rig.Admin.SetStoreAsync(rig.Harbor.Id, 5);

        var view = (await rig.Admin.GetAsync()).Value!;
        Assert.Equal(10m, view.DefaultPercent);
        Assert.Equal((5m, 5m), (view.Stores.Single(s => s.StoreId == rig.Harbor.Id).OverridePercent, view.Stores.Single(s => s.StoreId == rig.Harbor.Id).EffectivePercent));
        Assert.Equal((null, 10m), (view.Stores.Single(s => s.StoreId == rig.Other.Id).OverridePercent, view.Stores.Single(s => s.StoreId == rig.Other.Id).EffectivePercent));

        var cleared = (await rig.Admin.SetStoreAsync(rig.Harbor.Id, null)).Value!;
        Assert.Equal(10m, cleared.Stores.Single(s => s.StoreId == rig.Harbor.Id).EffectivePercent);
    }

    [Fact]
    public async Task A_made_up_store_is_not_found_and_zero_is_a_valid_percent()
    {
        var rig = Create();

        Assert.Equal(CommissionStatus.NotFound, (await rig.Admin.SetStoreAsync(Guid.NewGuid(), 5)).Status);
        Assert.Equal(CommissionStatus.Succeeded, (await rig.Admin.SetStoreAsync(rig.Harbor.Id, 0)).Status);
        Assert.Equal(0m, (await rig.Admin.GetAsync()).Value!.Stores.Single(s => s.StoreId == rig.Harbor.Id).EffectivePercent);
    }

    // ---------------- fixed on the order ----------------

    [Fact]
    public async Task An_order_records_the_percent_and_the_amount_on_the_product_total_not_the_fees()
    {
        var rig = Create(100);
        await rig.Admin.SetDefaultAsync(15);

        var order = await rig.Place();

        Assert.Equal((15m, 30m), (order.CommissionPercent, order.CommissionAmount));
        Assert.Equal(200m, order.SubtotalAmount);
    }

    [Fact]
    public async Task The_store_price_and_an_offer_are_already_in_the_total_the_commission_is_taken_from()
    {
        var rig = Create(100);
        await rig.Admin.SetStoreAsync(rig.Harbor.Id, 10);
        rig.Data.Inventory.Single(row => row.StoreId == rig.Harbor.Id && row.VariantId == rig.Data.DefaultVariantOf(rig.Tomato).Id).PriceOverride = 80;

        var order = await rig.Place();

        Assert.Equal((160m, 16m), (order.SubtotalAmount, order.CommissionAmount));
    }

    [Fact]
    public async Task Changing_the_percent_later_never_changes_an_order_already_placed()
    {
        var rig = Create();
        await rig.Admin.SetDefaultAsync(10);
        var first = await rig.Place();

        await rig.Admin.SetDefaultAsync(25);
        var second = await rig.Place();

        Assert.Equal((10m, 20m), (first.CommissionPercent, first.CommissionAmount));
        Assert.Equal((25m, 50m), (second.CommissionPercent, second.CommissionAmount));
    }

    [Fact]
    public async Task A_zero_percent_is_recorded_as_zero_and_without_a_setting_nothing_is_recorded()
    {
        var rig = Create();
        var zero = await rig.Place();
        var none = await rig.Place(rig.CheckoutWithoutCommission);

        Assert.Equal((0m, 0m), (zero.CommissionPercent, zero.CommissionAmount));
        Assert.Equal((null, null), (none.CommissionPercent, none.CommissionAmount));
    }

    // ---------------- the report ----------------

    private static ReportOrderRow Row(Guid store, OrderStatus status, decimal subtotal, decimal? commission, decimal delivery = 25, decimal handling = 5) =>
        new(Guid.NewGuid(), "N", store, Guid.NewGuid(), DateTime.UtcNow, status, "CashOnDelivery", PaymentState.NotRequired, subtotal, delivery, handling, subtotal + delivery + handling, commission);

    [Fact]
    public void Earnings_count_delivered_orders_only_and_the_store_keeps_the_fees()
    {
        var a = Guid.NewGuid();
        var b = Guid.NewGuid();
        var rows = new[]
        {
            Row(a, OrderStatus.Delivered, 200, 30),
            Row(a, OrderStatus.Completed, 100, 15),
            Row(a, OrderStatus.Cancelled, 500, 75),
            Row(a, OrderStatus.Pending, 500, 75),
            Row(b, OrderStatus.Delivered, 400, 20)
        };

        var earnings = ReportMath.Earnings(rows, new Dictionary<Guid, string> { [a] = "Alpha", [b] = "Beta" });

        var alpha = earnings.Single(row => row.StoreId == a);
        Assert.Equal((2, 300m, 45m, 60m, 315m), (alpha.Orders, alpha.Sales, alpha.Commission, alpha.Fees, alpha.Net));
        var beta = earnings.Single(row => row.StoreId == b);
        Assert.Equal((1, 400m, 20m, 30m, 410m), (beta.Orders, beta.Sales, beta.Commission, beta.Fees, beta.Net));
        Assert.Equal(["Alpha", "Beta"], earnings.Select(row => row.Store));
    }

    [Fact]
    public void Orders_from_before_commission_show_sales_and_no_commission_and_a_quiet_shop_shows_zeros()
    {
        var a = Guid.NewGuid();
        var quiet = Guid.NewGuid();

        var earnings = ReportMath.Earnings([Row(a, OrderStatus.Delivered, 200, null)], new Dictionary<Guid, string> { [a] = "Alpha", [quiet] = "Quiet" });

        Assert.Equal((1, 200m, 0m), (earnings.Single(row => row.StoreId == a).Orders, earnings.Single(row => row.StoreId == a).Sales, earnings.Single(row => row.StoreId == a).Commission));
        Assert.Equal((0, 0m, 0m, 0m), (earnings.Single(row => row.StoreId == quiet).Orders, earnings.Single(row => row.StoreId == quiet).Sales, earnings.Single(row => row.StoreId == quiet).Commission, earnings.Single(row => row.StoreId == quiet).Net));
    }

    // ---------------- SQL ----------------

    [Fact]
    public async Task On_SQL_the_percents_are_stored_the_database_refuses_over_100_and_orders_and_the_report_carry_the_commission()
    {
        if (CheckoutEfIntegrationTests.ConnectionString is null)
        {
            return;
        }

        await using var world = await CheckoutEfIntegrationTests.World.CreateAsync(1, 5);
        try
        {
            await using (var db = world.Db())
            {
                var store = new EfCommissionStore(db);
                await store.SetAsync(null, 12m, "t", DateTime.UtcNow);
                await store.SetAsync(world.Store.Id, 20m, "t", DateTime.UtcNow);
                await store.SetAsync(world.Store.Id, 25m, "t", DateTime.UtcNow);
                Assert.Equal(25m, (await store.GetOverridesAsync())[world.Store.Id]);
                Assert.Equal(12m, await store.GetDefaultAsync());
                await Assert.ThrowsAnyAsync<Exception>(async () => { await db.CommissionRates.Where(rate => rate.StoreId == world.Store.Id).ExecuteUpdateAsync(set => set.SetProperty(rate => rate.Percent, 101m)); });
            }

            var placed = await world.Checkout(world.CustomerIds[0], CheckoutEfIntegrationTests.Commit(null) with { CommissionPercent = 25m });

            Assert.Equal(CheckoutCommitStatus.Succeeded, placed.Status);
            await using var read = world.Db();
            var order = await read.Orders.AsNoTracking().SingleAsync(o => o.StoreId == world.Store.Id);
            Assert.Equal((25m, 7.50m), (order.CommissionPercent, order.CommissionAmount)); // 30 x 25%
            await read.Orders.Where(o => o.Id == order.Id).ExecuteUpdateAsync(set => set.SetProperty(o => o.Status, OrderStatus.Delivered));
            var organization = await read.Stores.Where(s => s.Id == world.Store.Id).Select(s => s.OrganizationId).SingleAsync();
            var rows = await new EfReportStore(read).GetOrderRowsAsync(organization, [world.Store.Id], DateTime.UtcNow.AddDays(-1), DateTime.UtcNow.AddDays(1));
            Assert.Equal(7.50m, rows.Single().CommissionAmount);
        }
        finally
        {
            await using var cleanup = world.Db();
            await cleanup.CommissionRates.Where(rate => rate.StoreId == world.Store.Id).ExecuteDeleteAsync();
        }
    }
}
