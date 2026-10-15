using System.Security.Claims;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using QuickCommerce.Api.Security;
using QuickCommerce.Application;
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

/// <summary>P22 phase A: a store manager sets the store's own price for a pack size. The shared price shows struck through when the store's is lower.</summary>
public sealed class StorePriceTests
{
    private sealed class Who : ICurrentUserContextResolver
    {
        public UserContext? Context { get; set; }
        public Task<UserContext?> ResolveAsync(CancellationToken cancellationToken = default) => Task.FromResult(Context);
    }

    private sealed class NoCache : ICacheService
    {
        public Task<T?> GetAsync<T>(string key, CancellationToken cancellationToken = default) => Task.FromResult(default(T));
        public Task SetAsync<T>(string key, T value, TimeSpan ttl, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task RemoveAsync(string key, CancellationToken cancellationToken = default) => Task.CompletedTask;
    }

    private sealed class Rig
    {
        public required InMemoryCommerceStore Data { get; init; }
        public required Who Who { get; init; }
        public required Store Harbor { get; init; }
        public required Store Other { get; init; }
        public required Product Tomato { get; init; }
        public required ProductVariant Kilo { get; init; }
        public required StockService Stock { get; init; }
        public required CatalogService Catalogue { get; init; }
        public required CartService Cart { get; init; }
        public required CheckoutService Checkout { get; init; }
        public Guid Org => Data.Organizations.Single().Id;

        public void SignInStaff(Store store) => Who.Context = new UserContext(Data.Users.Single().Id, Org, store.Id, Role.StoreStaff);
        public void SignInAdmin() => Who.Context = new UserContext(Data.Users.Single().Id, Org, null, Role.Admin);
        public Task<CatalogAdminResult<StockRowResponse>> SetPrice(decimal? price, bool carried = true, Guid? store = null) => Stock.SetAsync(store, Kilo.Id, new SetStockRequest(carried, 10, 5, price));
        public async Task<CatalogProductResponse> ShownAt(Store store) => (await Catalogue.GetCatalogProductForStoreAsync(Tomato.Id, store.Id)).Product!;
    }

    private static Rig Create(decimal basePrice = 100, decimal? mrp = null)
    {
        var data = InMemoryCommerceStore.CreateSeeded();
        var harbor = data.Stores.Single(store => store.Name == "Harbor Point Dark Store");
        var other = data.Stores.First(store => store.Id != harbor.Id);
        var tomato = data.Products.Single(product => product.Name == "Tomato");
        var kilo = data.DefaultVariantOf(tomato);
        kilo.Price = basePrice;
        kilo.Mrp = mrp;
        var user = data.Users.Single();
        var who = new Who();
        var currentUser = new HttpContextCurrentUser(new HttpContextAccessor
        {
            HttpContext = new DefaultHttpContext
            {
                User = new ClaimsPrincipal(new ClaimsIdentity(
                [
                    new Claim(ClaimTypes.NameIdentifier, user.ExternalSubject),
                    new Claim("organization_id", data.Organizations.Single().Id.ToString())
                ], "Test"))
            }
        });
        var resolver = new CurrentUserContextResolver(currentUser, data);
        var rig = new Rig
        {
            Data = data,
            Who = who,
            Harbor = harbor,
            Other = other,
            Tomato = tomato,
            Kilo = kilo,
            Stock = new StockService(data, who),
            Catalogue = new CatalogService(data, new StoreSelectionService(), new NoCache(), new DeliveryEstimator(new DeliverySettings()), null, null, data),
            Cart = new CartService(data, resolver, new AddCartItemRequestValidator(), new UpdateCartItemRequestValidator(), null, data),
            Checkout = new CheckoutService(data, resolver, new CheckoutRequestValidator())
        };
        rig.SignInStaff(harbor);
        return rig;
    }

    // ---------------- who may set it, and what is allowed ----------------

    [Fact]
    public async Task A_manager_sets_and_clears_the_stores_own_price_and_the_stock_list_shows_it()
    {
        var rig = Create(100, 125);

        var set = await rig.SetPrice(80);
        var listed = (await rig.Stock.ListAsync(null)).Value!.Single(row => row.VariantId == rig.Kilo.Id);
        var cleared = await rig.SetPrice(null);

        Assert.Equal(80m, set.Value!.PriceOverride);
        Assert.Equal((80m, 100m, 125m), (listed.PriceOverride, listed.BasePrice, listed.Mrp));
        Assert.Null(cleared.Value!.PriceOverride);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-5)]
    [InlineData(100001)]
    [InlineData(12.345)]
    public async Task A_store_price_must_be_above_zero_within_the_limit_with_two_decimals(double price)
    {
        var rig = Create();

        var result = await rig.SetPrice((decimal)price);

        Assert.Equal(CatalogAdminStatus.Invalid, result.Status);
        Assert.Null(rig.Data.Inventory.Single(row => row.StoreId == rig.Harbor.Id && row.VariantId == rig.Kilo.Id).PriceOverride);
    }

    [Fact]
    public async Task Staff_change_only_their_own_store_and_an_administrator_names_the_store()
    {
        var rig = Create();
        await rig.SetPrice(80, store: rig.Other.Id); // staff are always put on their own store, whatever they ask for
        Assert.Equal(80m, rig.Data.Inventory.Single(row => row.StoreId == rig.Harbor.Id && row.VariantId == rig.Kilo.Id).PriceOverride);
        Assert.Null(rig.Data.Inventory.Single(row => row.StoreId == rig.Other.Id && row.VariantId == rig.Kilo.Id).PriceOverride);

        rig.SignInAdmin();
        await rig.SetPrice(70, store: rig.Other.Id);
        Assert.Equal(70m, rig.Data.Inventory.Single(row => row.StoreId == rig.Other.Id && row.VariantId == rig.Kilo.Id).PriceOverride);
    }

    [Fact]
    public async Task A_customer_cannot_set_a_price()
    {
        var rig = Create();
        rig.Who.Context = new UserContext(rig.Data.Users.Single().Id, rig.Org, null, Role.Customer);

        Assert.Equal(CatalogAdminStatus.Forbidden, (await rig.SetPrice(1)).Status);
    }

    [Fact]
    public async Task A_price_on_a_pack_the_store_stops_carrying_is_dropped()
    {
        var rig = Create();
        await rig.SetPrice(80);

        await rig.SetPrice(80, carried: false);

        Assert.DoesNotContain(rig.Data.Inventory, row => row.StoreId == rig.Harbor.Id && row.VariantId == rig.Kilo.Id);
    }

    // ---------------- what the customer sees ----------------

    [Fact]
    public async Task A_lower_store_price_shows_the_shared_price_struck_through()
    {
        var rig = Create(100);
        await rig.SetPrice(80);

        var product = await rig.ShownAt(rig.Harbor);

        Assert.Equal((80m, 100m, 20), (product.Price, product.Mrp, product.DiscountPercent));
        var variant = product.Variants!.Single(item => item.IsDefault);
        Assert.Equal((80m, 100m, 20), (variant.Price, variant.Mrp, variant.DiscountPercent));
    }

    [Fact]
    public async Task A_real_mrp_is_the_struck_through_figure()
    {
        var rig = Create(100, 125);
        await rig.SetPrice(80);

        var product = await rig.ShownAt(rig.Harbor);

        Assert.Equal((80m, 125m, 36), (product.Price, product.Mrp, product.DiscountPercent));
    }

    [Fact]
    public async Task A_higher_store_price_has_nothing_struck_through()
    {
        var rig = Create(100);
        await rig.SetPrice(120);

        var product = await rig.ShownAt(rig.Harbor);

        Assert.Equal((120m, 120m, 0), (product.Price, product.Mrp, product.DiscountPercent));
    }

    [Fact]
    public async Task Another_store_keeps_the_shared_price()
    {
        var rig = Create(100);
        await rig.SetPrice(80);

        Assert.Equal(100m, (await rig.ShownAt(rig.Other)).Price);
    }

    [Fact]
    public async Task Clearing_the_store_price_brings_back_the_shared_price()
    {
        var rig = Create(100);
        await rig.SetPrice(80);
        await rig.SetPrice(null);

        Assert.Equal((100m, 100m), ((await rig.ShownAt(rig.Harbor)).Price, (await rig.ShownAt(rig.Harbor)).Mrp));
    }

    [Fact]
    public async Task An_offer_takes_its_percent_off_the_store_price_and_the_old_figure_stays()
    {
        var rig = Create(100);
        await rig.SetPrice(80);
        rig.Data.Campaigns.Add(new Campaign
        {
            TitleEn = "Sale",
            BodyEn = "x",
            DiscountPercent = 25,
            StartsAt = DateTime.UtcNow.AddHours(-1),
            EndsAt = DateTime.UtcNow.AddDays(1),
            Products = [new CampaignProduct { ProductId = rig.Tomato.Id }]
        });

        var product = await rig.ShownAt(rig.Harbor);

        Assert.Equal((60m, 100m, 40, 25), (product.Price, product.Mrp, product.DiscountPercent, product.OfferPercent));
    }

    // ---------------- cart and checkout charge it ----------------

    [Fact]
    public async Task The_cart_and_checkout_charge_the_store_price_and_the_receipt_keeps_the_shared_price()
    {
        var rig = Create(100);
        await rig.SetPrice(80);

        var cart = (await rig.Cart.AddItemAsync(rig.Harbor.Id, new AddCartItemRequest(rig.Tomato.Id, 2))).Cart!;
        var result = await rig.Checkout.CheckoutAsync(rig.Harbor.Id, new CheckoutRequest("12 Main Street", 19.07, 72.87));

        var line = Assert.Single(cart.Items);
        Assert.Equal((80m, 80m, 100m, 160m), (line.UnitPriceSnapshot, line.CurrentUnitPrice, line.OriginalUnitPrice, line.TotalPrice));
        Assert.Equal(CheckoutOperationStatus.Succeeded, result.Status);
        var item = Assert.Single(rig.Data.Orders.Single().Items);
        Assert.Equal((80m, 100m), (item.UnitPrice, item.UnitMrpSnapshot));
    }

    [Fact]
    public async Task When_the_manager_changes_the_price_after_it_was_added_checkout_asks_again_and_repricing_fixes_it()
    {
        var rig = Create(100);
        await rig.Cart.AddItemAsync(rig.Harbor.Id, new AddCartItemRequest(rig.Tomato.Id, 1));
        await rig.SetPrice(90);

        var refused = await rig.Checkout.CheckoutAsync(rig.Harbor.Id, new CheckoutRequest("12 Main Street", 19.07, 72.87));
        await rig.Cart.RepriceAsync(rig.Harbor.Id);
        var accepted = await rig.Checkout.CheckoutAsync(rig.Harbor.Id, new CheckoutRequest("12 Main Street", 19.07, 72.87));

        Assert.Equal(CheckoutReasons.PriceChanged, refused.Reason);
        Assert.Equal(CheckoutOperationStatus.Succeeded, accepted.Status);
        Assert.Equal(90m, rig.Data.Orders.Single().Items.Single().UnitPrice);
    }

    [Fact]
    public async Task A_cart_at_another_store_pays_the_shared_price()
    {
        var rig = Create(100);
        await rig.SetPrice(80);

        var cart = (await rig.Cart.AddItemAsync(rig.Other.Id, new AddCartItemRequest(rig.Tomato.Id, 1))).Cart!;

        Assert.Equal((100m, null), (cart.Items[0].UnitPriceSnapshot, cart.Items[0].OriginalUnitPrice));
    }

    // ---------------- SQL ----------------

    [Fact]
    public async Task On_SQL_the_price_is_stored_the_database_refuses_zero_and_checkout_charges_it()
    {
        if (CheckoutEfIntegrationTests.ConnectionString is null)
        {
            return;
        }

        await using var world = await CheckoutEfIntegrationTests.World.CreateAsync(1, 5);
        await using (var db = world.Db())
        {
            await new EfCatalogAdminStore(db).SetStockAsync(world.Store.Id, world.Kilo.Id, true, 5, 5, 24m);
        }

        await using (var db = world.Db())
        {
            Assert.Equal(24m, (await db.StoreVariantInventory.AsNoTracking().SingleAsync(row => row.StoreId == world.Store.Id && row.VariantId == world.Kilo.Id)).PriceOverride);
        }

        var stale = await world.Checkout(world.CustomerIds[0], CheckoutEfIntegrationTests.Commit(null));
        Assert.Equal(CheckoutCommitStatus.PriceChanged, stale.Status);
        Assert.Equal((30m, 24m), (stale.Issues![0].OldPrice, stale.Issues[0].NewPrice));
        await using (var db = world.Db())
        {
            await db.CartItems.Where(item => item.ProductId == world.Product.Id).ExecuteUpdateAsync(set => set.SetProperty(item => item.UnitPriceSnapshot, 24m));
        }

        Assert.Equal(CheckoutCommitStatus.Succeeded, (await world.Checkout(world.CustomerIds[0], CheckoutEfIntegrationTests.Commit(null))).Status);
        await using (var db = world.Db())
        {
            var item = await db.Orders.Where(order => order.StoreId == world.Store.Id).SelectMany(order => order.Items).SingleAsync();
            Assert.Equal(24m, item.UnitPrice);
            await Assert.ThrowsAnyAsync<Exception>(async () =>
            {
                await db.StoreVariantInventory.Where(row => row.StoreId == world.Store.Id).ExecuteUpdateAsync(set => set.SetProperty(row => row.PriceOverride, 0m));
            });
        }
    }
}
