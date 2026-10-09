using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using QuickCommerce.Application.DTOs;
using QuickCommerce.Application.Interfaces;
using QuickCommerce.Application.Services;
using QuickCommerce.Domain;
using QuickCommerce.Infrastructure.Persistence;
using QuickCommerce.Infrastructure.Security;
using Xunit;

namespace QuickCommerce.Tests;

/// <summary>
/// Phase P5 against a real SQL Server: one order per checkout key even when taps arrive together, the fee columns, the rules the
/// database itself enforces, and the cart queries. Opt-in, like the other SQL tests: set QUICKCOMMERCE_TEST_CONNECTION_STRING to a
/// connection string for a DISPOSABLE database. Everything it adds is uniquely named and removed again. Never point it at a database you care about.
/// </summary>
public sealed class CheckoutEfIntegrationTests
{
    private static readonly PricingSettings Fees = new() { DeliveryFee = 25, HandlingFee = 5, FreeDeliveryThreshold = 199 };

    internal static string? ConnectionString => Environment.GetEnvironmentVariable("QUICKCOMMERCE_TEST_CONNECTION_STRING") is { Length: > 0 } text ? text : null;

    internal static CheckoutCommit Commit(string? key, string hash = "hash-a") =>
        new(new CheckoutDelivery("12 Test Street", 41.0, 41.0, null, "Asha", "9876543210"), Fees, key, key is null ? null : hash);

    internal sealed class World : IAsyncDisposable
    {
        public required DbContextOptions<QuickCommerceDbContext> Options { get; init; }
        public required string Token { get; init; }
        public required Category Category { get; init; }
        public required Product Product { get; init; }
        public required Store Store { get; init; }
        public required ProductVariant Kilo { get; init; }
        public required string[] Emails { get; init; }
        public required List<Guid> CustomerIds { get; init; }

        public static async Task<World> CreateAsync(int customers, int stock, int cartQuantity = 1)
        {
            var options = new DbContextOptionsBuilder<QuickCommerceDbContext>().UseSqlServer(ConnectionString!).Options;
            await using (var migrate = new QuickCommerceDbContext(options))
            {
                await migrate.Database.MigrateAsync();
            }

            var token = "co" + Guid.NewGuid().ToString("N")[..10];
            Guid organizationId;
            await using (var lookup = new QuickCommerceDbContext(options))
            {
                organizationId = (await lookup.Organizations.AsNoTracking().FirstAsync()).Id;
            }

            var category = new Category { Name = $"Cat {token}" };
            var product = new Product { Sku = $"{token}-P", Name = $"{token} Tomato", Description = "checkout", Price = 30, UnitOfMeasure = "1 kg", CategoryId = category.Id };
            var store = new Store { Name = $"Store {token}", Address = "test", Latitude = 41.0, Longitude = 41.0, ServiceRadiusKm = 5, OrganizationId = organizationId };
            var kilo = new ProductVariant { ProductId = product.Id, Sku = product.Sku, Label = "1 kg", Price = 30, IsDefault = true };
            var emails = Enumerable.Range(0, customers).Select(index => $"{token}-{index}@example.test").ToArray();

            await using (var seed = new QuickCommerceDbContext(options))
            {
                seed.Categories.Add(category);
                seed.Products.Add(product);
                seed.Stores.Add(store);
                await seed.SaveChangesAsync();
                seed.ProductVariants.Add(kilo);
                await seed.SaveChangesAsync();
                seed.StoreVariantInventory.Add(new StoreVariantInventory { StoreId = store.Id, VariantId = kilo.Id, AvailableQuantity = stock });
                await seed.SaveChangesAsync();
            }

            foreach (var email in emails)
            {
                await using var db = new QuickCommerceDbContext(options);
                Assert.True(await new EfAccountStore(db).TryCreateCustomerAsync(new NewCustomerAccount(organizationId, "Buyer", "Buyer", token, email, null, "x")));
            }

            var customerIds = new List<Guid>();
            await using (var db = new QuickCommerceDbContext(options))
            {
                foreach (var email in emails)
                {
                    var userId = await db.Users.Where(user => user.ExternalSubject == email).Select(user => user.Id).SingleAsync();
                    customerIds.Add(await db.Customers.Where(customer => customer.UserId == userId).Select(customer => customer.Id).SingleAsync());
                }

                foreach (var customerId in customerIds)
                {
                    var cart = new Cart { CustomerId = customerId, StoreId = store.Id };
                    cart.Items.Add(new CartItem { ProductId = product.Id, VariantId = kilo.Id, ProductNameSnapshot = product.Name, VariantLabelSnapshot = "1 kg", UnitPriceSnapshot = 30, Quantity = cartQuantity });
                    db.Carts.Add(cart);
                }

                await db.SaveChangesAsync();
            }

            return new World { Options = options, Token = token, Category = category, Product = product, Store = store, Kilo = kilo, Emails = emails, CustomerIds = customerIds };
        }

        public QuickCommerceDbContext Db() => new(Options);

        public async Task<CheckoutCommitResult> Checkout(Guid customerId, CheckoutCommit commit)
        {
            await using var db = Db();
            return await new EfCommerceStore(db).TryCheckoutCartAsync(customerId, Store.Id, commit);
        }

        public async Task PutBack(Guid customerId, int quantity = 1)
        {
            await using var db = Db();
            var cart = await db.Carts.Include(c => c.Items).SingleOrDefaultAsync(c => c.CustomerId == customerId && c.StoreId == Store.Id);
            if (cart is null)
            {
                cart = new Cart { CustomerId = customerId, StoreId = Store.Id };
                db.Carts.Add(cart);
            }

            cart.Items.Add(new CartItem { ProductId = Product.Id, VariantId = Kilo.Id, ProductNameSnapshot = Product.Name, VariantLabelSnapshot = "1 kg", UnitPriceSnapshot = 30, Quantity = quantity });
            await db.SaveChangesAsync();
        }

        public async Task<int> Stock()
        {
            await using var db = Db();
            return (await db.StoreVariantInventory.AsNoTracking().SingleAsync(row => row.StoreId == Store.Id && row.VariantId == Kilo.Id)).AvailableQuantity;
        }

        public async ValueTask DisposeAsync()
        {
            await using var cleanup = Db();
            var userIds = await cleanup.Users.Where(user => Emails.Contains(user.ExternalSubject)).Select(user => user.Id).ToListAsync();
            var cartIds = await cleanup.Carts.Where(cart => cart.StoreId == Store.Id).Select(cart => cart.Id).ToListAsync();
            await cleanup.CartItems.Where(item => cartIds.Contains(item.CartId)).ExecuteDeleteAsync();
            await cleanup.Carts.Where(cart => cart.StoreId == Store.Id).ExecuteDeleteAsync();
            var orderIds = await cleanup.Orders.Where(order => order.StoreId == Store.Id).Select(order => order.Id).ToListAsync();
            await cleanup.CheckoutRequests.Where(record => orderIds.Contains(record.OrderId)).ExecuteDeleteAsync();
            await cleanup.Notifications.Where(notification => notification.OrderId != null && orderIds.Contains(notification.OrderId.Value)).ExecuteDeleteAsync();
            await cleanup.Orders.Where(order => order.StoreId == Store.Id).ExecuteDeleteAsync();
            await cleanup.StoreVariantInventory.Where(row => row.StoreId == Store.Id).ExecuteDeleteAsync();
            await cleanup.ProductVariants.Where(variant => variant.ProductId == Product.Id).ExecuteDeleteAsync();
            await cleanup.Products.Where(row => row.Id == Product.Id).ExecuteDeleteAsync();
            await cleanup.Stores.Where(row => row.Id == Store.Id).ExecuteDeleteAsync();
            await cleanup.Categories.Where(row => row.Id == Category.Id).ExecuteDeleteAsync();
            await cleanup.PasswordResetCodes.Where(code => userIds.Contains(code.UserId)).ExecuteDeleteAsync();
            await cleanup.RefreshTokens.Where(refresh => userIds.Contains(refresh.UserId)).ExecuteDeleteAsync();
            await cleanup.UserRoles.Where(role => userIds.Contains(role.UserId)).ExecuteDeleteAsync();
            await cleanup.UserCredentials.Where(credential => userIds.Contains(credential.UserId)).ExecuteDeleteAsync();
            await cleanup.Customers.Where(customer => userIds.Contains(customer.UserId)).ExecuteDeleteAsync();
            await cleanup.Users.Where(user => userIds.Contains(user.Id)).ExecuteDeleteAsync();
        }
    }

    [Fact]
    public async Task Taps_arriving_together_with_one_key_make_exactly_one_order_and_take_stock_once()
    {
        if (ConnectionString is null)
        {
            return;
        }

        await using var world = await World.CreateAsync(customers: 1, stock: 10, cartQuantity: 2);
        var customer = world.CustomerIds.Single();

        var results = await Task.WhenAll(Enumerable.Range(0, 8).Select(_ => world.Checkout(customer, Commit("tap-key-0001-abcd"))));

        Assert.All(results, result => Assert.Equal(CheckoutCommitStatus.Succeeded, result.Status));
        Assert.Single(results.Select(result => result.Order!.Id).Distinct());
        Assert.Equal(1, results.Count(result => !result.Replayed));
        Assert.Equal(8, await world.Stock());
        await using var verify = world.Db();
        Assert.Equal(1, await verify.Orders.CountAsync(order => order.StoreId == world.Store.Id));
        Assert.Equal(1, await verify.CheckoutRequests.CountAsync(record => record.CustomerId == customer));
    }

    [Fact]
    public async Task Taps_with_different_keys_for_one_cart_still_make_one_order()
    {
        if (ConnectionString is null)
        {
            return;
        }

        await using var world = await World.CreateAsync(customers: 1, stock: 10);
        var customer = world.CustomerIds.Single();

        var results = await Task.WhenAll(Enumerable.Range(0, 6).Select(index => world.Checkout(customer, Commit($"distinct-key-{index:D4}-abcd"))));

        Assert.Equal(1, results.Count(result => result.Status == CheckoutCommitStatus.Succeeded));
        Assert.Equal(5, results.Count(result => result.Status == CheckoutCommitStatus.CartEmpty));
        Assert.Equal(9, await world.Stock());
    }

    [Fact]
    public async Task A_repeat_returns_the_stored_order_with_its_fees_and_receiver_and_another_place_is_refused()
    {
        if (ConnectionString is null)
        {
            return;
        }

        await using var world = await World.CreateAsync(customers: 1, stock: 10);
        var customer = world.CustomerIds.Single();

        var first = await world.Checkout(customer, Commit("repeat-key-0001-abcd"));
        var again = await world.Checkout(customer, Commit("repeat-key-0001-abcd"));
        var other = await world.Checkout(customer, Commit("repeat-key-0001-abcd", hash: "hash-b"));

        Assert.False(first.Replayed);
        Assert.True(again.Replayed);
        Assert.Equal(first.Order!.Id, again.Order!.Id);
        Assert.Equal(CheckoutCommitStatus.KeyReused, other.Status);
        var order = again.Order;
        Assert.Equal((30m, 25m, 5m, 60m, "CashOnDelivery", "Asha", "9876543210"), (order.SubtotalAmount, order.DeliveryFee, order.HandlingFee, order.TotalAmount, order.PaymentMethod, order.ReceiverName, order.ReceiverPhone));
        Assert.Single(order.Items);
        Assert.Single(order.StatusHistory);
        Assert.Equal(9, await world.Stock());
    }

    [Fact]
    public async Task A_key_older_than_a_day_counts_as_new()
    {
        if (ConnectionString is null)
        {
            return;
        }

        await using var world = await World.CreateAsync(customers: 1, stock: 10);
        var customer = world.CustomerIds.Single();
        var first = await world.Checkout(customer, Commit("old-key-00001-abcdef"));
        await using (var age = world.Db())
        {
            await age.CheckoutRequests.Where(record => record.CustomerId == customer).ExecuteUpdateAsync(set => set.SetProperty(record => record.CreatedAt, DateTime.UtcNow.AddHours(-25)));
        }

        await world.PutBack(customer);
        var later = await world.Checkout(customer, Commit("old-key-00001-abcdef"));

        Assert.Equal(CheckoutCommitStatus.Succeeded, later.Status);
        Assert.False(later.Replayed);
        Assert.NotEqual(first.Order!.Id, later.Order!.Id);
        await using var verify = world.Db();
        Assert.Equal(1, await verify.CheckoutRequests.CountAsync(record => record.CustomerId == customer));
    }

    [Fact]
    public async Task Without_a_key_nothing_is_recorded_and_fees_are_still_stored()
    {
        if (ConnectionString is null)
        {
            return;
        }

        await using var world = await World.CreateAsync(customers: 1, stock: 10, cartQuantity: 7);
        var customer = world.CustomerIds.Single();

        var result = await world.Checkout(customer, Commit(null));

        Assert.Equal(CheckoutCommitStatus.Succeeded, result.Status);
        Assert.Equal((210m, 0m, 5m, 215m), (result.Order!.SubtotalAmount, result.Order.DeliveryFee, result.Order.HandlingFee, result.Order.TotalAmount));
        await using var verify = world.Db();
        Assert.Equal(0, await verify.CheckoutRequests.CountAsync(record => record.CustomerId == customer));
    }

    [Fact]
    public async Task Several_short_lines_are_all_reported_and_stock_is_untouched()
    {
        if (ConnectionString is null)
        {
            return;
        }

        await using var world = await World.CreateAsync(customers: 1, stock: 1, cartQuantity: 3);
        var customer = world.CustomerIds.Single();

        var result = await world.Checkout(customer, Commit("short-key-0001-abcd"));

        Assert.Equal(CheckoutCommitStatus.InventoryConflict, result.Status);
        var issue = Assert.Single(result.Issues!);
        Assert.Equal((3, 1), (issue.Quantity, issue.Available));
        Assert.Equal(1, await world.Stock());
        await using var verify = world.Db();
        Assert.Equal(0, await verify.CheckoutRequests.CountAsync(record => record.CustomerId == customer));
    }

    [Fact]
    public async Task The_database_refuses_a_second_order_for_one_key_and_negative_fees()
    {
        if (ConnectionString is null)
        {
            return;
        }

        await using var world = await World.CreateAsync(customers: 1, stock: 10);
        var customer = world.CustomerIds.Single();
        var placed = await world.Checkout(customer, Commit("rule-key-00001-abcdef"));

        await using (var db = world.Db())
        {
            db.CheckoutRequests.Add(new CheckoutRequestRecord { CustomerId = customer, IdempotencyKey = "rule-key-00001-abcdef", RequestHash = "x", OrderId = placed.Order!.Id });
            var duplicate = await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync());
            Assert.Contains("UX_CheckoutRequests_CustomerKey", duplicate.InnerException!.Message);
        }

        await using (var db = world.Db())
        {
            var error = await Assert.ThrowsAsync<SqlException>(() => db.Database.ExecuteSqlInterpolatedAsync($"UPDATE Orders SET DeliveryFee = -1 WHERE Id = {placed.Order!.Id}"));
            Assert.Contains("CK_Orders_Fees", error.Message);
        }
    }

    [Fact]
    public async Task The_current_cart_is_the_latest_one_and_deleting_removes_its_lines()
    {
        if (ConnectionString is null)
        {
            return;
        }

        await using var world = await World.CreateAsync(customers: 1, stock: 10);
        var customer = world.CustomerIds.Single();
        await using var db = world.Db();
        var store = new EfCommerceStore(db);

        var current = await store.GetCurrentCartAsync(customer);
        Assert.Equal(world.Store.Id, current!.StoreId);
        Assert.Single(current.Items);

        Assert.True(await store.DeleteCartAsync(customer, world.Store.Id));
        Assert.False(await store.DeleteCartAsync(customer, world.Store.Id));
        Assert.Null(await store.GetCurrentCartAsync(customer));
        await using var verify = world.Db();
        Assert.False(await verify.CartItems.AnyAsync(item => item.ProductId == world.Product.Id && world.CustomerIds.Contains(item.Cart.CustomerId)));
    }

    [Fact]
    public async Task Orders_placed_before_fees_existed_read_back_with_subtotal_equal_to_total()
    {
        if (ConnectionString is null)
        {
            return;
        }

        await using var world = await World.CreateAsync(customers: 1, stock: 10);
        var customer = world.CustomerIds.Single();
        Guid userId;
        await using (var lookup = world.Db())
        {
            userId = await lookup.Customers.Where(c => c.Id == customer).Select(c => c.UserId).SingleAsync();
        }

        // What the migration leaves behind for an old order: no subtotal, no fees, total set.
        var orderId = Guid.NewGuid();
        await using (var db = world.Db())
        {
            await db.Database.ExecuteSqlInterpolatedAsync($@"
INSERT INTO Orders (Id, OrderNumber, UserId, StoreId, TotalAmount, SubtotalAmount, DeliveryFee, HandlingFee, PaymentMethod, Status, DeliveryAddress, Latitude, Longitude, CreatedAt)
VALUES ({orderId}, {world.Token + "-old"}, {userId}, {world.Store.Id}, 120, 0, 0, 0, 'CashOnDelivery', 'Completed', 'old', 41, 41, SYSUTCDATETIME())");
            await db.Database.ExecuteSqlInterpolatedAsync($"UPDATE Orders SET SubtotalAmount = TotalAmount WHERE Id = {orderId}");
        }

        await using var read = world.Db();
        var order = await read.Orders.AsNoTracking().SingleAsync(o => o.Id == orderId);
        Assert.Equal((120m, 120m, 0m, 0m, "CashOnDelivery", null), (order.TotalAmount, order.SubtotalAmount, order.DeliveryFee, order.HandlingFee, order.PaymentMethod, order.ReceiverName));
    }
}
