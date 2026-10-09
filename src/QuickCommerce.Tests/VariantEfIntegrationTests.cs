using Microsoft.EntityFrameworkCore;
using QuickCommerce.Application.DTOs;
using QuickCommerce.Application.Interfaces;
using QuickCommerce.Domain;
using QuickCommerce.Infrastructure.Persistence;
using QuickCommerce.Infrastructure.Security;
using Xunit;

namespace QuickCommerce.Tests;

/// <summary>
/// Phase P3 against a real SQL Server: concurrent checkout of the last units of one pack size, the order snapshots, and the
/// database rules. Opt-in, like the other SQL tests: set QUICKCOMMERCE_TEST_CONNECTION_STRING to a connection string for a
/// DISPOSABLE database. Everything it adds is uniquely named and removed again. Never point it at a database you care about.
/// </summary>
public sealed class VariantEfIntegrationTests
{
    private const int Racers = 6;
    private const int UnitsInStock = 2;

    [Fact]
    public async Task Parallel_checkouts_never_oversell_a_variant_and_orders_snapshot_the_pack()
    {
        var connectionString = Environment.GetEnvironmentVariable("QUICKCOMMERCE_TEST_CONNECTION_STRING");
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            return;
        }

        var options = new DbContextOptionsBuilder<QuickCommerceDbContext>().UseSqlServer(connectionString).Options;
        await using (var migrate = new QuickCommerceDbContext(options))
        {
            await migrate.Database.MigrateAsync();
        }

        var token = "vr" + Guid.NewGuid().ToString("N")[..10];
        Guid organizationId;
        await using (var lookup = new QuickCommerceDbContext(options))
        {
            organizationId = (await lookup.Organizations.AsNoTracking().FirstAsync()).Id;
        }

        var category = new Category { Name = $"Cat {token}" };
        var product = new Product { Sku = $"{token}-P", Name = $"{token} Tomato", Description = "variants", Price = 30, UnitOfMeasure = "1 kg", CategoryId = category.Id };
        var store = new Store { Name = $"Store {token}", Address = "test", Latitude = 41.0, Longitude = 41.0, ServiceRadiusKm = 5, OrganizationId = organizationId };
        var kilo = new ProductVariant { ProductId = product.Id, Sku = product.Sku, Label = "1 kg", Price = 30, IsDefault = true };
        var half = new ProductVariant { ProductId = product.Id, Sku = product.Sku + "-500", Label = "500 g", Price = 16, Mrp = 18, SortOrder = 1 };
        var emails = Enumerable.Range(0, Racers).Select(index => $"{token}-{index}@example.test").ToArray();

        await using (var seed = new QuickCommerceDbContext(options))
        {
            seed.Categories.Add(category);
            seed.Products.Add(product);
            seed.Stores.Add(store);
            await seed.SaveChangesAsync();
            seed.ProductVariants.AddRange(kilo, half);
            await seed.SaveChangesAsync();
            seed.StoreVariantInventory.AddRange(
                new StoreVariantInventory { StoreId = store.Id, VariantId = kilo.Id, AvailableQuantity = 50 },
                new StoreVariantInventory { StoreId = store.Id, VariantId = half.Id, AvailableQuantity = UnitsInStock });
            await seed.SaveChangesAsync();
        }

        foreach (var email in emails)
        {
            await using var db = new QuickCommerceDbContext(options);
            Assert.True(await new EfAccountStore(db).TryCreateCustomerAsync(new NewCustomerAccount(organizationId, "Racer", "Racer", token, email, null, "x")));
        }

        var customerIds = new List<Guid>();
        await using (var db = new QuickCommerceDbContext(options))
        {
            foreach (var email in emails)
            {
                var userId = await db.Users.Where(user => user.ExternalSubject == email).Select(user => user.Id).SingleAsync();
                customerIds.Add(await db.Customers.Where(customer => customer.UserId == userId).Select(customer => customer.Id).SingleAsync());
            }

            // Every customer has one 500 g pack in their cart: six carts, two packs in stock.
            foreach (var customerId in customerIds)
            {
                var cart = new Cart { CustomerId = customerId, StoreId = store.Id };
                cart.Items.Add(new CartItem { ProductId = product.Id, VariantId = half.Id, ProductNameSnapshot = product.Name, VariantLabelSnapshot = "500 g", UnitPriceSnapshot = 16, Quantity = 1 });
                db.Carts.Add(cart);
            }

            await db.SaveChangesAsync();
        }

        try
        {
            var request = new CheckoutCommit(new CheckoutDelivery("12 Test Street", 41.0, 41.0), new QuickCommerce.Application.Services.PricingSettings());
            var attempts = customerIds.Select(async customerId =>
            {
                await using var db = new QuickCommerceDbContext(options);
                return await new EfCommerceStore(db).TryCheckoutCartAsync(customerId, store.Id, request);
            }).ToArray();
            var results = await Task.WhenAll(attempts);

            Assert.Equal(UnitsInStock, results.Count(result => result.Status == CheckoutCommitStatus.Succeeded));
            Assert.Equal(Racers - UnitsInStock, results.Count(result => result.Status == CheckoutCommitStatus.InventoryConflict));

            await using var verify = new QuickCommerceDbContext(options);
            var stock = await verify.StoreVariantInventory.AsNoTracking().SingleAsync(row => row.StoreId == store.Id && row.VariantId == half.Id);
            Assert.Equal(0, stock.AvailableQuantity);
            Assert.Equal(50, (await verify.StoreVariantInventory.AsNoTracking().SingleAsync(row => row.StoreId == store.Id && row.VariantId == kilo.Id)).AvailableQuantity);

            // Exactly the successful checkouts produced orders, each snapshotting the pack; losing carts are left intact.
            var orders = await verify.Orders.AsNoTracking().Include(order => order.Items).Where(order => order.StoreId == store.Id).ToListAsync();
            Assert.Equal(UnitsInStock, orders.Count);
            Assert.All(orders.SelectMany(order => order.Items), line => Assert.Equal((half.Id, "500 g", 16m, 18m), (line.VariantId!.Value, line.VariantLabelSnapshot, line.UnitPrice, line.UnitMrpSnapshot!.Value)));
            Assert.Equal(Racers - UnitsInStock, await verify.CartItems.CountAsync(item => item.VariantId == half.Id && customerIds.Contains(item.Cart.CustomerId)));

            // The admin order path takes stock from the same row with the same protection.
            var winnerUser = orders[0].UserId;
            await using (var refill = new QuickCommerceDbContext(options))
            {
                await refill.StoreVariantInventory.Where(row => row.StoreId == store.Id && row.VariantId == half.Id).ExecuteUpdateAsync(set => set.SetProperty(row => row.AvailableQuantity, UnitsInStock));
            }

            var adminAttempts = Enumerable.Range(0, Racers).Select(async index =>
            {
                await using var db = new QuickCommerceDbContext(options);
                var order = new Order
                {
                    OrderNumber = $"{token}-{index}"[..Math.Min(32, token.Length + 2)],
                    UserId = winnerUser,
                    StoreId = store.Id,
                    DeliveryAddress = "admin order",
                    TotalAmount = 16,
                    Items = [new OrderItem { ProductId = product.Id, VariantId = half.Id, ProductNameSnapshot = product.Name, VariantLabelSnapshot = "500 g", UnitPrice = 16, Quantity = 1 }]
                };
                return await new EfCommerceStore(db).TryCreateOrderAsync(order, [new InventoryAdjustment(store.Id, half.Id, 1)]);
            }).ToArray();
            var adminResults = await Task.WhenAll(adminAttempts);
            Assert.Equal(UnitsInStock, adminResults.Count(created => created));

            await using var after = new QuickCommerceDbContext(options);
            Assert.Equal(0, (await after.StoreVariantInventory.AsNoTracking().SingleAsync(row => row.StoreId == store.Id && row.VariantId == half.Id)).AvailableQuantity);

            // A later price edit does not touch placed orders; a repriced variant stops a cart that still holds the old price.
            await using (var reprice = new QuickCommerceDbContext(options))
            {
                await reprice.ProductVariants.Where(variant => variant.Id == half.Id).ExecuteUpdateAsync(set => set.SetProperty(variant => variant.Price, 99m).SetProperty(variant => variant.Mrp, (decimal?)120m));
            }

            await using var snapshots = new QuickCommerceDbContext(options);
            Assert.All(await snapshots.OrderItems.AsNoTracking().Where(item => item.VariantId == half.Id).ToListAsync(), line => Assert.Equal(16m, line.UnitPrice));
            await using var priceCheck = new QuickCommerceDbContext(options);
            await priceCheck.StoreVariantInventory.Where(row => row.StoreId == store.Id && row.VariantId == half.Id).ExecuteUpdateAsync(set => set.SetProperty(row => row.AvailableQuantity, 5));
            await using var stale = new QuickCommerceDbContext(options);
            var loserCustomerId = await snapshots.CartItems.Where(item => item.VariantId == half.Id && customerIds.Contains(item.Cart.CustomerId)).Select(item => item.Cart.CustomerId).FirstAsync();
            var staleResult = await new EfCommerceStore(stale).TryCheckoutCartAsync(loserCustomerId, store.Id, request);
            Assert.Equal(CheckoutCommitStatus.PriceChanged, staleResult.Status);

            // Orders placed before variants existed have no variant and still load.
            await using var legacyWrite = new QuickCommerceDbContext(options);
            var legacy = new Order
            {
                OrderNumber = $"{token}-old"[..Math.Min(32, token.Length + 4)],
                UserId = winnerUser,
                StoreId = store.Id,
                DeliveryAddress = "legacy",
                TotalAmount = 30,
                Items = [new OrderItem { ProductId = product.Id, ProductNameSnapshot = product.Name, UnitPrice = 30, Quantity = 1 }]
            };
            legacyWrite.Orders.Add(legacy);
            await legacyWrite.SaveChangesAsync();
            await using var legacyRead = new QuickCommerceDbContext(options);
            var loaded = (await new EfCommerceStore(legacyRead).GetOrderAsync(legacy.Id))!;
            Assert.Null(loaded.Items.Single().VariantId);
            Assert.Null(loaded.Items.Single().VariantLabelSnapshot);
        }
        finally
        {
            await using var cleanup = new QuickCommerceDbContext(options);
            var userIds = await cleanup.Users.Where(user => emails.Contains(user.ExternalSubject)).Select(user => user.Id).ToListAsync();
            var cartIds = await cleanup.Carts.Where(cart => cart.StoreId == store.Id).Select(cart => cart.Id).ToListAsync();
            await cleanup.CartItems.Where(item => cartIds.Contains(item.CartId)).ExecuteDeleteAsync();
            await cleanup.Carts.Where(cart => cart.StoreId == store.Id).ExecuteDeleteAsync();
            var orderIds = await cleanup.Orders.Where(order => order.StoreId == store.Id).Select(order => order.Id).ToListAsync();
            await cleanup.Notifications.Where(notification => notification.OrderId != null && orderIds.Contains(notification.OrderId.Value)).ExecuteDeleteAsync();
            await cleanup.Orders.Where(order => order.StoreId == store.Id).ExecuteDeleteAsync();
            await cleanup.StoreVariantInventory.Where(row => row.StoreId == store.Id).ExecuteDeleteAsync();
            await cleanup.ProductVariants.Where(variant => variant.ProductId == product.Id).ExecuteDeleteAsync();
            await cleanup.Products.Where(row => row.Id == product.Id).ExecuteDeleteAsync();
            await cleanup.Stores.Where(row => row.Id == store.Id).ExecuteDeleteAsync();
            await cleanup.Categories.Where(row => row.Id == category.Id).ExecuteDeleteAsync();
            await cleanup.PasswordResetCodes.Where(code => userIds.Contains(code.UserId)).ExecuteDeleteAsync();
            await cleanup.RefreshTokens.Where(refresh => userIds.Contains(refresh.UserId)).ExecuteDeleteAsync();
            await cleanup.UserRoles.Where(role => userIds.Contains(role.UserId)).ExecuteDeleteAsync();
            await cleanup.UserCredentials.Where(credential => userIds.Contains(credential.UserId)).ExecuteDeleteAsync();
            await cleanup.Customers.Where(customer => userIds.Contains(customer.UserId)).ExecuteDeleteAsync();
            await cleanup.Users.Where(user => userIds.Contains(user.Id)).ExecuteDeleteAsync();
        }
    }
}
