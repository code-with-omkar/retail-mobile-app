using Microsoft.EntityFrameworkCore;
using QuickCommerce.Application.DTOs;
using QuickCommerce.Application.Interfaces;
using QuickCommerce.Application.Services;
using QuickCommerce.Domain;
using QuickCommerce.Infrastructure.Persistence;
using Xunit;
using static QuickCommerce.Tests.CheckoutEfIntegrationTests;

namespace QuickCommerce.Tests;

/// <summary>
/// Coupons against a real SQL Server (opt-in, like the other SQL tests): applied inside the order's transaction, limits that hold even when
/// customers race, and the use given back when an order is cancelled, rejected or its payment hold runs out.
/// </summary>
[Collection("SqlServer")]
public sealed class CouponEfTests
{
    // Fees of the existing checkout tests: delivery 25 + handling 5, free from 199. A cart of one item is 30, so an order is 60.
    private static CheckoutCommit WithCode(string code, string? key = null, string method = PaymentMethods.CashOnDelivery) =>
        Commit(key ?? "k" + Guid.NewGuid().ToString("N")[..12]) with { CouponCode = code, PaymentMethod = method };

    private static async Task<Coupon> MakeCoupon(World world, string kind, decimal value, Action<Coupon>? tune = null, decimal? cap = null)
    {
        await using var db = world.Db();
        var organizationId = await db.Stores.Where(store => store.Id == world.Store.Id).Select(store => store.OrganizationId).SingleAsync();
        var coupon = new Coupon { OrganizationId = organizationId, Code = ("T" + Guid.NewGuid().ToString("N")[..10]).ToUpperInvariant(), Kind = kind, Value = value, MaxDiscount = cap, StartsAt = DateTime.UtcNow.AddDays(-1) };
        tune?.Invoke(coupon);
        db.Coupons.Add(coupon);
        await db.SaveChangesAsync();
        return coupon;
    }

    private static async Task Drop(World world, Coupon coupon)
    {
        await using var db = world.Db();
        await db.CouponRedemptions.Where(item => item.CouponId == coupon.Id).ExecuteDeleteAsync();
        await db.Coupons.Where(item => item.Id == coupon.Id).ExecuteDeleteAsync();
    }

    private static async Task<Coupon> Reload(World world, Coupon coupon)
    {
        await using var db = world.Db();
        return await db.Coupons.AsNoTracking().SingleAsync(item => item.Id == coupon.Id);
    }

    private static async Task<(Guid UserId, Guid OrganizationId)> UserOf(World world, Guid customerId)
    {
        await using var db = world.Db();
        return (await db.Customers.Where(customer => customer.Id == customerId).Select(customer => customer.UserId).SingleAsync(),
            await db.Stores.Where(store => store.Id == world.Store.Id).Select(store => store.OrganizationId).SingleAsync());
    }

    [Fact]
    public async Task A_percent_coupon_takes_off_the_total_is_kept_on_the_order_and_counts_as_a_use()
    {
        if (ConnectionString is null) { return; }
        await using var world = await World.CreateAsync(1, 10);
        var coupon = await MakeCoupon(world, CouponKinds.Percent, 10, cap: 5);
        try
        {
            var result = await world.Checkout(world.CustomerIds[0], WithCode(coupon.Code));

            Assert.Equal(CheckoutCommitStatus.Succeeded, result.Status);
            Assert.Equal(57m, result.Order!.TotalAmount); // 30 + 25 + 5, less 10% of 30
            Assert.Equal(3m, result.Order.CouponDiscount);
            await using var db = world.Db();
            var order = await db.Orders.AsNoTracking().SingleAsync(item => item.Id == result.Order.Id);
            Assert.Equal(coupon.Code, order.CouponCode);
            Assert.Equal(3m, order.CouponDiscount);
            Assert.Equal(57m, order.TotalAmount);
            Assert.Equal(30m, order.SubtotalAmount);
            var redemption = await db.CouponRedemptions.AsNoTracking().SingleAsync(item => item.OrderId == order.Id);
            Assert.Equal(3m, redemption.Amount);
            Assert.Null(redemption.ReleasedAt);
            Assert.Equal(1, (await Reload(world, coupon)).UsedCount);
        }
        finally { await Drop(world, coupon); }
    }

    [Fact]
    public async Task Flat_is_capped_by_the_products_and_free_delivery_takes_off_only_the_delivery_fee()
    {
        if (ConnectionString is null) { return; }
        await using var world = await World.CreateAsync(2, 10);
        var flat = await MakeCoupon(world, CouponKinds.Flat, 50);
        var delivery = await MakeCoupon(world, CouponKinds.FreeDelivery, 0);
        try
        {
            var a = await world.Checkout(world.CustomerIds[0], WithCode(flat.Code));
            var b = await world.Checkout(world.CustomerIds[1], WithCode(delivery.Code));

            await using var db = world.Db();
            var orderA = await db.Orders.AsNoTracking().SingleAsync(item => item.Id == a.Order!.Id);
            var orderB = await db.Orders.AsNoTracking().SingleAsync(item => item.Id == b.Order!.Id);
            Assert.Equal(30m, orderA.CouponDiscount); // 50 would be more than the 30 of products
            Assert.Equal(30m, orderA.TotalAmount);    // the two fees remain
            Assert.Equal(25m, orderB.CouponDiscount);
            Assert.Equal(35m, orderB.TotalAmount);    // 30 + 5 handling
            Assert.Equal(25m, orderB.DeliveryFee);    // the fee itself is unchanged; the coupon is shown as its own line
        }
        finally { await Drop(world, flat); await Drop(world, delivery); }
    }

    [Fact]
    public async Task A_refused_code_changes_nothing_no_order_no_stock_taken_the_cart_stays()
    {
        if (ConnectionString is null) { return; }
        await using var world = await World.CreateAsync(1, 10);
        var minimum = await MakeCoupon(world, CouponKinds.Flat, 10, c => c.MinSubtotal = 500);
        try
        {
            var unknown = await world.Checkout(world.CustomerIds[0], WithCode("NOSUCHCODE"));
            var below = await world.Checkout(world.CustomerIds[0], WithCode(minimum.Code));

            Assert.Equal(CheckoutCommitStatus.CouponRejected, unknown.Status);
            Assert.Equal(CheckoutCommitStatus.CouponRejected, below.Status);
            Assert.Contains("more", below.Message);
            Assert.Equal(10, await world.Stock());
            await using var db = world.Db();
            Assert.Empty(await db.Orders.Where(item => item.StoreId == world.Store.Id).ToListAsync());
            Assert.Single(await db.CartItems.Where(item => db.Carts.Any(cart => cart.Id == item.CartId && cart.StoreId == world.Store.Id)).ToListAsync());
            Assert.Equal(0, (await Reload(world, minimum)).UsedCount);
        }
        finally { await Drop(world, minimum); }
    }

    [Fact]
    public async Task Customers_racing_for_the_last_use_cannot_both_get_it()
    {
        if (ConnectionString is null) { return; }
        await using var world = await World.CreateAsync(4, 10);
        var coupon = await MakeCoupon(world, CouponKinds.Flat, 5, c => c.TotalLimit = 1);
        try
        {
            var results = await Task.WhenAll(world.CustomerIds.Select(id => world.Checkout(id, WithCode(coupon.Code))));

            Assert.Equal(1, results.Count(r => r.Status == CheckoutCommitStatus.Succeeded));
            Assert.Equal(3, results.Count(r => r.Status == CheckoutCommitStatus.CouponRejected));
            Assert.Equal(1, (await Reload(world, coupon)).UsedCount);
            await using var db = world.Db();
            Assert.Equal(1, await db.Orders.CountAsync(item => item.StoreId == world.Store.Id));
            Assert.Equal(9, await world.Stock()); // a refused checkout gives its stock back
        }
        finally { await Drop(world, coupon); }
    }

    [Fact]
    public async Task The_per_customer_limit_counts_that_customers_uses_only()
    {
        if (ConnectionString is null) { return; }
        await using var world = await World.CreateAsync(2, 10);
        var coupon = await MakeCoupon(world, CouponKinds.Flat, 5, c => c.PerCustomerLimit = 1);
        try
        {
            Assert.Equal(CheckoutCommitStatus.Succeeded, (await world.Checkout(world.CustomerIds[0], WithCode(coupon.Code))).Status);
            await world.PutBack(world.CustomerIds[0]);
            var again = await world.Checkout(world.CustomerIds[0], WithCode(coupon.Code));
            var other = await world.Checkout(world.CustomerIds[1], WithCode(coupon.Code));

            Assert.Equal(CheckoutCommitStatus.CouponRejected, again.Status);
            Assert.Contains("already used", again.Message);
            Assert.Equal(CheckoutCommitStatus.Succeeded, other.Status);
        }
        finally { await Drop(world, coupon); }
    }

    [Fact]
    public async Task First_order_only_lets_a_new_customer_in_and_turns_back_one_who_has_ordered()
    {
        if (ConnectionString is null) { return; }
        await using var world = await World.CreateAsync(1, 10);
        var coupon = await MakeCoupon(world, CouponKinds.Flat, 5, c => c.FirstOrderOnly = true);
        try
        {
            Assert.Equal(CheckoutCommitStatus.Succeeded, (await world.Checkout(world.CustomerIds[0], WithCode(coupon.Code))).Status);
            await world.PutBack(world.CustomerIds[0]);
            var second = await world.Checkout(world.CustomerIds[0], WithCode(coupon.Code));
            Assert.Equal(CheckoutCommitStatus.CouponRejected, second.Status);
        }
        finally { await Drop(world, coupon); }
    }

    [Fact]
    public async Task Cancelling_gives_the_use_back_so_the_customer_can_use_the_code_again()
    {
        if (ConnectionString is null) { return; }
        await using var world = await World.CreateAsync(1, 10);
        var coupon = await MakeCoupon(world, CouponKinds.Flat, 5, c => { c.PerCustomerLimit = 1; c.TotalLimit = 1; });
        try
        {
            var first = await world.Checkout(world.CustomerIds[0], WithCode(coupon.Code));
            var (userId, organizationId) = await UserOf(world, world.CustomerIds[0]);
            await using (var db = world.Db())
            {
                var cancelled = await new EfCommerceStore(db).TryCancelCustomerOrderAsync(first.Order!.Id, userId, organizationId);
                Assert.Equal(CancelCommitStatus.Cancelled, cancelled.Status);
            }

            Assert.Equal(0, (await Reload(world, coupon)).UsedCount);
            await using (var db = world.Db())
            {
                Assert.NotNull((await db.CouponRedemptions.AsNoTracking().SingleAsync(item => item.OrderId == first.Order.Id)).ReleasedAt);
            }

            await world.PutBack(world.CustomerIds[0]);
            Assert.Equal(CheckoutCommitStatus.Succeeded, (await world.Checkout(world.CustomerIds[0], WithCode(coupon.Code))).Status);
            Assert.Equal(1, (await Reload(world, coupon)).UsedCount);
        }
        finally { await Drop(world, coupon); }
    }

    [Fact]
    public async Task Cancelling_twice_gives_the_use_back_only_once()
    {
        if (ConnectionString is null) { return; }
        await using var world = await World.CreateAsync(2, 10);
        var coupon = await MakeCoupon(world, CouponKinds.Flat, 5);
        try
        {
            var first = await world.Checkout(world.CustomerIds[0], WithCode(coupon.Code));
            await world.Checkout(world.CustomerIds[1], WithCode(coupon.Code));
            var (userId, organizationId) = await UserOf(world, world.CustomerIds[0]);
            for (var i = 0; i < 2; i++)
            {
                await using var db = world.Db();
                await new EfCommerceStore(db).TryCancelCustomerOrderAsync(first.Order!.Id, userId, organizationId);
            }

            Assert.Equal(1, (await Reload(world, coupon)).UsedCount); // the other customer's use remains
        }
        finally { await Drop(world, coupon); }
    }

    [Fact]
    public async Task A_store_rejecting_the_order_gives_the_use_back()
    {
        if (ConnectionString is null) { return; }
        await using var world = await World.CreateAsync(1, 10);
        var coupon = await MakeCoupon(world, CouponKinds.Flat, 5);
        try
        {
            var placed = await world.Checkout(world.CustomerIds[0], WithCode(coupon.Code));
            var (_, organizationId) = await UserOf(world, world.CustomerIds[0]);
            await using (var db = world.Db())
            {
                var rejected = await new EfCommerceStore(db).TryTransitionOrderAsync(placed.Order!.Id, organizationId, world.Store.Id, OrderStatus.Rejected);
                Assert.Equal(OrderLifecycleStatus.Succeeded, rejected.Status);
            }

            Assert.Equal(0, (await Reload(world, coupon)).UsedCount);
        }
        finally { await Drop(world, coupon); }
    }

    [Fact]
    public async Task An_online_order_that_is_never_paid_gives_the_use_back_when_the_hold_runs_out()
    {
        if (ConnectionString is null) { return; }
        await using var world = await World.CreateAsync(1, 10);
        var coupon = await MakeCoupon(world, CouponKinds.Flat, 5);
        try
        {
            var placed = await world.Checkout(world.CustomerIds[0], WithCode(coupon.Code, method: PaymentMethods.Online) with { PaymentExpiresAt = DateTime.UtcNow.AddMinutes(-1) });
            Assert.Equal(CheckoutCommitStatus.Succeeded, placed.Status);
            Assert.Equal(1, (await Reload(world, coupon)).UsedCount);

            await using (var db = world.Db())
            {
                var released = await new EfPaymentStore(db).ReleaseExpiredAsync(DateTime.UtcNow, 50);
                Assert.Contains(placed.Order!.Id, released);
            }

            Assert.Equal(0, (await Reload(world, coupon)).UsedCount);
        }
        finally { await Drop(world, coupon); }
    }

    [Fact]
    public async Task A_code_that_would_make_an_online_order_free_is_refused_but_cash_on_delivery_may_be_free()
    {
        if (ConnectionString is null) { return; }
        await using var world = await World.CreateAsync(2, 10);
        var coupon = await MakeCoupon(world, CouponKinds.Percent, 100);
        try
        {
            var noFees = new PricingSettings();
            var online = await world.Checkout(world.CustomerIds[0], WithCode(coupon.Code, method: PaymentMethods.Online) with { Pricing = noFees });
            var cash = await world.Checkout(world.CustomerIds[1], WithCode(coupon.Code) with { Pricing = noFees });

            Assert.Equal(CheckoutCommitStatus.CouponRejected, online.Status);
            Assert.Contains("whole order", online.Message);
            Assert.Equal(CheckoutCommitStatus.Succeeded, cash.Status);
            Assert.Equal(0m, cash.Order!.TotalAmount);
        }
        finally { await Drop(world, coupon); }
    }

    [Fact]
    public async Task A_switched_off_or_expired_code_is_refused_but_orders_that_used_it_keep_their_discount()
    {
        if (ConnectionString is null) { return; }
        await using var world = await World.CreateAsync(2, 10);
        var coupon = await MakeCoupon(world, CouponKinds.Flat, 5);
        try
        {
            var first = await world.Checkout(world.CustomerIds[0], WithCode(coupon.Code));
            await using (var db = world.Db())
            {
                await db.Coupons.Where(item => item.Id == coupon.Id).ExecuteUpdateAsync(set => set.SetProperty(item => item.IsActive, false));
            }

            var second = await world.Checkout(world.CustomerIds[1], WithCode(coupon.Code));

            Assert.Equal(CheckoutCommitStatus.CouponRejected, second.Status);
            await using var check = world.Db();
            Assert.Equal(5m, (await check.Orders.AsNoTracking().SingleAsync(item => item.Id == first.Order!.Id)).CouponDiscount);
        }
        finally { await Drop(world, coupon); }
    }

    [Fact]
    public async Task The_service_lists_creates_and_changes_coupons_for_an_administrator_and_previews_a_code_for_a_customer()
    {
        if (ConnectionString is null) { return; }
        await using var world = await World.CreateAsync(1, 10);
        var code = ("S" + Guid.NewGuid().ToString("N")[..9]).ToUpperInvariant();
        Guid? created = null;
        try
        {
            await using var db = world.Db();
            var organizationId = await db.Stores.Where(store => store.Id == world.Store.Id).Select(store => store.OrganizationId).SingleAsync();
            var (userId, _) = await UserOf(world, world.CustomerIds[0]);
            var admin = new FixedScope(true, organizationId);
            var customer = new FixedContext(new UserContext(userId, organizationId, null, Role.Customer));
            var cartService = new FixedCart(await CartOf(world, world.CustomerIds[0]));
            var store = new EfCouponStore(db);
            var service = new CouponService(store, admin, customer, cartService);

            var made = await service.CreateAsync(new SaveCouponRequest(" " + code.ToLowerInvariant() + " ", CouponKinds.Percent, 10, MaxDiscount: 100, MinSubtotal: 20));
            Assert.Equal(CouponServiceStatus.Succeeded, made.Status);
            created = made.Value!.Id;
            Assert.Equal(code, made.Value.Code);
            Assert.Equal("Active", made.Value.Status);

            Assert.Equal(CouponServiceStatus.Conflict, (await service.CreateAsync(new SaveCouponRequest(code, CouponKinds.Flat, 5))).Status);
            Assert.Equal(CouponServiceStatus.Invalid, (await service.CreateAsync(new SaveCouponRequest("X", CouponKinds.Flat, 5))).Status);

            var preview = await service.PreviewAsync(world.Store.Id, code.ToLowerInvariant());
            Assert.True(preview.Value!.Valid);
            Assert.Equal(3m, preview.Value.Discount);
            Assert.Equal(57m, preview.Value.Total); // the cart total (60) less 3

            var changed = await service.UpdateAsync(created.Value, new SaveCouponRequest(code, CouponKinds.Percent, 10, MaxDiscount: 100, MinSubtotal: 20, IsActive: false));
            Assert.Equal("Off", changed.Value!.Status);
            Assert.False((await service.PreviewAsync(world.Store.Id, code)).Value!.Valid);

            Assert.Contains((await service.ListAsync()).Value!, item => item.Code == code);

            var notAdmin = new CouponService(store, new FixedScope(false, organizationId), customer, cartService);
            Assert.Equal(CouponServiceStatus.Forbidden, (await notAdmin.ListAsync()).Status);
        }
        finally
        {
            if (created is { } id)
            {
                await using var cleanup = world.Db();
                await cleanup.Coupons.Where(item => item.Id == id).ExecuteDeleteAsync();
            }
        }
    }

    private static async Task<CartResponse> CartOf(World world, Guid customerId)
    {
        await using var db = world.Db();
        var cart = await db.Carts.AsNoTracking().Include(item => item.Items).SingleAsync(item => item.CustomerId == customerId && item.StoreId == world.Store.Id);
        var items = cart.Items.Select(i => new CartItemResponse(i.ProductId, i.ProductNameSnapshot, i.UnitPriceSnapshot, i.Quantity, i.TotalPrice, i.VariantId, i.VariantLabelSnapshot)).ToArray();
        var subtotal = items.Sum(i => i.TotalPrice);
        var price = PricingCalculator.Compute(new PricingSettings { DeliveryFee = 25, HandlingFee = 5, FreeDeliveryThreshold = 199 }, subtotal);
        return new CartResponse(cart.Id, customerId, world.Store.Id, cart.CreatedAt, cart.UpdatedAt, items, subtotal, price.Subtotal, price.DeliveryFee, price.HandlingFee, price.Total, price.FreeDeliveryThreshold, price.AmountToFreeDelivery);
    }

    private sealed class FixedScope(bool admin, Guid organizationId) : IAuthorizationScopeService
    {
        private AuthorizationScope Scope => new(Guid.NewGuid(), organizationId, new HashSet<string> { admin ? "ApplicationAdmin" : "StoreStaff" }, new HashSet<Guid>(), new HashSet<string>());
        public Task<AuthorizationScope?> ResolveAsync(CancellationToken cancellationToken = default) => Task.FromResult<AuthorizationScope?>(Scope);
        public Task<AuthorizationScope?> ResolveForUserAsync(Guid userId, CancellationToken cancellationToken = default) => Task.FromResult<AuthorizationScope?>(Scope);
        public Task<bool> HasPermissionAsync(string permissionCode, CancellationToken cancellationToken = default) => Task.FromResult(admin);
        public Task<bool> CanAccessStoreAsync(Guid storeId, CancellationToken cancellationToken = default) => Task.FromResult(admin);
        public Task<bool> OwnsCustomerAsync(Guid customerId, CancellationToken cancellationToken = default) => Task.FromResult(false);
    }

    private sealed class FixedContext(UserContext context) : ICurrentUserContextResolver
    {
        public Task<UserContext?> ResolveAsync(CancellationToken cancellationToken = default) => Task.FromResult<UserContext?>(context);
    }

    private sealed class FixedCart(CartResponse cart) : ICartService
    {
        public Task<CartResponse?> GetCartAsync(Guid storeId, CancellationToken cancellationToken = default) => Task.FromResult<CartResponse?>(cart);
        public Task<CartResponse?> GetCurrentCartAsync(CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<CartOperationResult> AddItemAsync(Guid storeId, AddCartItemRequest request, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<CartOperationResult> UpdateItemAsync(Guid storeId, Guid productId, UpdateCartItemRequest request, Guid? variantId = null, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<CartOperationResult> RemoveItemAsync(Guid storeId, Guid productId, Guid? variantId = null, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<CartOperationResult> ClearAsync(Guid storeId, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<CartOperationResult> MergeAsync(Guid storeId, MergeCartRequest request, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<CartOperationResult> RepriceAsync(Guid storeId, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }
}
