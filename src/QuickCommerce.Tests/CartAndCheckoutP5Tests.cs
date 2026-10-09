using System.Security.Claims;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using QuickCommerce.Api.Controllers;
using QuickCommerce.Api.Security;
using QuickCommerce.Application.DTOs;
using QuickCommerce.Application.Interfaces;
using QuickCommerce.Application.Services;
using QuickCommerce.Application.Validators;
using QuickCommerce.Domain;
using QuickCommerce.Infrastructure;
using QuickCommerce.Infrastructure.Security;
using Xunit;

namespace QuickCommerce.Tests;

/// <summary>
/// Phase P5: the cart the server keeps, fees decided by the server, a checkout that cannot create the same order twice,
/// and specific reasons when checkout cannot go ahead. The SQL Server behaviour (parallel requests, indexes) is in CheckoutEfIntegrationTests.
/// </summary>
public sealed class CartAndCheckoutP5Tests
{
    private const string Key = "attempt-0001-abcdef";

    private static Store Harbor(InMemoryCommerceStore data) => data.Stores.Single(store => store.Name == "Harbor Point Dark Store");
    private static Product Tomato(InMemoryCommerceStore data) => data.Products.Single(product => product.Name == "Tomato");

    private static readonly PricingSettings Fees = new() { DeliveryFee = 25, HandlingFee = 5, FreeDeliveryThreshold = 199 };

    private sealed class OneAddressStore(CustomerAddress? address) : IAddressStore
    {
        public Task<AddressOwner?> FindOwnerAsync(Guid userId, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<IReadOnlyList<CustomerAddress>> ListAsync(Guid customerId, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<CustomerAddress?> GetAsync(Guid customerId, Guid addressId, CancellationToken cancellationToken = default) =>
            Task.FromResult(address is not null && address.CustomerId == customerId && address.Id == addressId ? address : null);
        public Task<AddAddressResult> AddAsync(Guid customerId, CustomerAddress address, int maxPerCustomer, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<CustomerAddress?> UpdateAsync(Guid customerId, Guid addressId, AddressFields fields, DateTime nowUtc, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<bool> SetDefaultAsync(Guid customerId, Guid addressId, DateTime nowUtc, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<bool> DeleteAsync(Guid customerId, Guid addressId, DateTime nowUtc, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }

    private static (CartService Cart, CheckoutService Checkout) Services(InMemoryCommerceStore data, PricingSettings? pricing = null, CustomerAddress? address = null)
    {
        var user = data.Users.Single();
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
        return (
            new CartService(data, resolver, new AddCartItemRequestValidator(), new UpdateCartItemRequestValidator(), pricing),
            new CheckoutService(data, resolver, new CheckoutRequestValidator(), pricing, new OneAddressStore(address)));
    }

    private static CheckoutRequest Where => new("12 Main Street", 19.07, 72.87);

    private static async Task Add(CartService cart, Store store, Product product, int quantity, Guid? variantId = null) =>
        Assert.Equal(CartOperationStatus.Succeeded, (await cart.AddItemAsync(store.Id, new AddCartItemRequest(product.Id, quantity, variantId))).Status);

    private static ProductVariant AddVariant(InMemoryCommerceStore data, Product product, string label, decimal price, int stock, Store store)
    {
        var variant = new ProductVariant { ProductId = product.Id, Sku = $"{product.Sku}-{label}", Label = label, Price = price, SortOrder = 1 };
        data.Variants.Add(variant);
        data.Inventory.Add(new StoreVariantInventory { StoreId = store.Id, VariantId = variant.Id, AvailableQuantity = stock });
        return variant;
    }

    // ---------------- fees ----------------

    [Theory]
    [InlineData(0, 0, 0, 0, 0)]
    [InlineData(100, 25, 5, 130, 99)]
    [InlineData(198.99, 25, 5, 228.99, 0.01)]
    [InlineData(199, 0, 5, 204, 0)]
    [InlineData(500, 0, 5, 505, 0)]
    public void Fees_follow_the_threshold(double subtotal, double delivery, double handling, double total, double toFree)
    {
        var price = PricingCalculator.Compute(Fees, (decimal)subtotal);

        Assert.Equal(((decimal)delivery, (decimal)handling, (decimal)total, (decimal)toFree), (price.DeliveryFee, price.HandlingFee, price.Total, price.AmountToFreeDelivery));
    }

    [Fact]
    public void Without_a_threshold_delivery_is_never_free_and_nothing_is_asked_for()
    {
        var price = PricingCalculator.Compute(new PricingSettings { DeliveryFee = 20 }, 1000);

        Assert.Equal((20m, 0m, 1020m, 0m), (price.DeliveryFee, price.HandlingFee, price.Total, price.AmountToFreeDelivery));
    }

    [Theory]
    [InlineData(-1, 0, 0)]
    [InlineData(0, -1, 0)]
    [InlineData(0, 0, -1)]
    [InlineData(20_000, 0, 0)]
    public void Unusable_fee_settings_stop_the_start_up(double delivery, double handling, double threshold)
    {
        var settings = new PricingSettings { DeliveryFee = (decimal)delivery, HandlingFee = (decimal)handling, FreeDeliveryThreshold = (decimal)threshold };

        Assert.Throws<InvalidOperationException>(settings.Validate);
    }

    [Fact]
    public async Task The_cart_returns_the_server_s_totals_and_what_is_missing_for_free_delivery()
    {
        var data = InMemoryCommerceStore.CreateSeeded();
        var store = Harbor(data);
        var tomato = Tomato(data);
        var (cart, _) = Services(data, Fees);
        await Add(cart, store, tomato, 2);

        var response = (await cart.GetCartAsync(store.Id))!;

        var subtotal = tomato.Price * 2;
        Assert.Equal(subtotal, response.Subtotal);
        Assert.Equal(subtotal, response.TotalAmount);
        Assert.Equal((25m, 5m, subtotal + 30m, 199m), (response.DeliveryFee, response.HandlingFee, response.Total, response.FreeDeliveryThreshold));
        Assert.Equal(199m - subtotal, response.AmountToFreeDelivery);
    }

    // ---------------- cart ----------------

    [Fact]
    public async Task Each_line_says_how_many_the_store_has_and_what_the_shop_charges_now()
    {
        var data = InMemoryCommerceStore.CreateSeeded();
        var store = Harbor(data);
        var tomato = Tomato(data);
        var (cart, _) = Services(data, Fees);
        await Add(cart, store, tomato, 1);
        var oldPrice = tomato.Price;
        data.DefaultVariantOf(tomato).Price = oldPrice + 3;
        data.StockOf(store, tomato).AvailableQuantity = 4;

        var line = (await cart.GetCartAsync(store.Id))!.Items.Single();

        Assert.Equal((oldPrice, oldPrice + 3, 4, false), (line.UnitPriceSnapshot, line.CurrentUnitPrice, line.Available, line.Unavailable));
    }

    [Fact]
    public async Task A_pack_that_is_no_longer_sold_is_marked_unavailable_in_the_cart()
    {
        var data = InMemoryCommerceStore.CreateSeeded();
        var store = Harbor(data);
        var tomato = Tomato(data);
        var half = AddVariant(data, tomato, "500 g", 15, 10, store);
        var (cart, _) = Services(data, Fees);
        await Add(cart, store, tomato, 1, half.Id);
        half.IsActive = false;

        var line = (await cart.GetCartAsync(store.Id))!.Items.Single();

        Assert.True(line.Unavailable);
        Assert.Null(line.CurrentUnitPrice);
    }

    [Fact]
    public async Task The_current_cart_is_found_without_naming_a_store_and_is_absent_when_there_is_none()
    {
        var data = InMemoryCommerceStore.CreateSeeded();
        var store = Harbor(data);
        var (cart, _) = Services(data, Fees);
        Assert.Null(await cart.GetCurrentCartAsync());

        await Add(cart, store, Tomato(data), 1);

        var current = (await cart.GetCurrentCartAsync())!;
        Assert.Equal(store.Id, current.StoreId);
        Assert.Single(current.Items);
    }

    [Fact]
    public async Task Clearing_deletes_the_cart_and_clearing_nothing_is_fine()
    {
        var data = InMemoryCommerceStore.CreateSeeded();
        var store = Harbor(data);
        var (cart, _) = Services(data, Fees);
        Assert.Equal(CartOperationStatus.Succeeded, (await cart.ClearAsync(store.Id)).Status);
        await Add(cart, store, Tomato(data), 3);

        var cleared = await cart.ClearAsync(store.Id);

        Assert.Equal(CartOperationStatus.Succeeded, cleared.Status);
        Assert.Empty(cleared.Cart!.Items);
        Assert.Null(await cart.GetCartAsync(store.Id));
        Assert.Null(await cart.GetCurrentCartAsync());
    }

    // ---------------- merging a guest cart ----------------

    [Fact]
    public async Task Merging_into_no_cart_creates_it_and_adds_quantities_for_the_same_pack()
    {
        var data = InMemoryCommerceStore.CreateSeeded();
        var store = Harbor(data);
        var tomato = Tomato(data);
        var half = AddVariant(data, tomato, "500 g", 15, 50, store);
        var (cart, _) = Services(data, Fees);

        var merged = await cart.MergeAsync(store.Id, new MergeCartRequest([
            new AddCartItemRequest(tomato.Id, 2),
            new AddCartItemRequest(tomato.Id, 1),
            new AddCartItemRequest(tomato.Id, 4, half.Id)]));

        Assert.Equal(CartOperationStatus.Succeeded, merged.Status);
        Assert.Empty(merged.Notes!);
        var lines = merged.Cart!.Items.ToDictionary(line => line.VariantLabel!);
        Assert.Equal(2, lines.Count);
        Assert.Equal(3, lines["1 kg"].Quantity);
        Assert.Equal(4, lines["500 g"].Quantity);
    }

    [Fact]
    public async Task Merging_adds_to_what_is_already_in_the_server_cart_without_repricing_it()
    {
        var data = InMemoryCommerceStore.CreateSeeded();
        var store = Harbor(data);
        var tomato = Tomato(data);
        var (cart, _) = Services(data, Fees);
        await Add(cart, store, tomato, 2);
        var oldPrice = tomato.Price;
        data.DefaultVariantOf(tomato).Price = oldPrice + 5;

        var merged = await cart.MergeAsync(store.Id, new MergeCartRequest([new AddCartItemRequest(tomato.Id, 3)]));

        var line = merged.Cart!.Items.Single();
        Assert.Equal((5, oldPrice, oldPrice + 5), (line.Quantity, line.UnitPriceSnapshot, line.CurrentUnitPrice));
    }

    [Fact]
    public async Task Merging_stops_at_what_the_store_has_and_tells_the_customer()
    {
        var data = InMemoryCommerceStore.CreateSeeded();
        var store = Harbor(data);
        var tomato = Tomato(data);
        var half = AddVariant(data, tomato, "500 g", 15, 3, store);
        var empty = AddVariant(data, tomato, "250 g", 8, 0, store);
        var (cart, _) = Services(data, Fees);

        var merged = await cart.MergeAsync(store.Id, new MergeCartRequest([
            new AddCartItemRequest(tomato.Id, 10, half.Id),
            new AddCartItemRequest(tomato.Id, 1, empty.Id)]));

        Assert.Equal(3, merged.Cart!.Items.Single().Quantity);
        Assert.Contains(merged.Notes!, note => note.Kind == CartNoteKinds.Reduced && note.VariantId == half.Id && note.Quantity == 3);
        Assert.Contains(merged.Notes!, note => note.Kind == CartNoteKinds.OutOfStock && note.VariantId == empty.Id);
    }

    [Fact]
    public async Task Merging_reports_products_that_are_gone_or_unknown_and_keeps_the_rest()
    {
        var data = InMemoryCommerceStore.CreateSeeded();
        var store = Harbor(data);
        var tomato = Tomato(data);
        var other = data.Products.First(product => product.Id != tomato.Id);
        other.IsActive = false;
        var (cart, _) = Services(data, Fees);

        var merged = await cart.MergeAsync(store.Id, new MergeCartRequest([
            new AddCartItemRequest(tomato.Id, 1),
            new AddCartItemRequest(other.Id, 1),
            new AddCartItemRequest(Guid.NewGuid(), 1)]));

        Assert.Equal(tomato.Id, merged.Cart!.Items.Single().ProductId);
        Assert.Equal(2, merged.Notes!.Count(note => note.Kind == CartNoteKinds.Unavailable));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(101)]
    public async Task Merging_nothing_or_too_much_is_refused(int count)
    {
        var data = InMemoryCommerceStore.CreateSeeded();
        var store = Harbor(data);
        var (cart, _) = Services(data, Fees);
        var items = Enumerable.Range(0, count).Select(_ => new AddCartItemRequest(Tomato(data).Id, 1)).ToArray();

        var result = await cart.MergeAsync(store.Id, new MergeCartRequest(items));

        Assert.Equal(CartOperationStatus.InvalidRequest, result.Status);
        Assert.Null(await cart.GetCartAsync(store.Id));
    }

    [Fact]
    public async Task Merging_a_bad_quantity_is_refused_before_anything_is_added()
    {
        var data = InMemoryCommerceStore.CreateSeeded();
        var store = Harbor(data);
        var (cart, _) = Services(data, Fees);

        var result = await cart.MergeAsync(store.Id, new MergeCartRequest([new AddCartItemRequest(Tomato(data).Id, 1), new AddCartItemRequest(Tomato(data).Id, 0)]));

        Assert.Equal(CartOperationStatus.InvalidRequest, result.Status);
        Assert.Null(await cart.GetCartAsync(store.Id));
    }

    // ---------------- repricing ----------------

    [Fact]
    public async Task Repricing_brings_every_line_to_the_current_price_and_leaves_gone_lines_for_the_customer_to_remove()
    {
        var data = InMemoryCommerceStore.CreateSeeded();
        var store = Harbor(data);
        var tomato = Tomato(data);
        var half = AddVariant(data, tomato, "500 g", 15, 10, store);
        var (cart, _) = Services(data, Fees);
        await Add(cart, store, tomato, 1);
        await Add(cart, store, tomato, 2, half.Id);
        data.DefaultVariantOf(tomato).Price += 4;
        half.IsActive = false;

        var repriced = await cart.RepriceAsync(store.Id);

        var lines = repriced.Cart!.Items.ToDictionary(line => line.VariantLabel!);
        Assert.Equal(lines["1 kg"].CurrentUnitPrice, lines["1 kg"].UnitPriceSnapshot);
        Assert.True(lines["500 g"].Unavailable);
        Assert.Equal(15m, lines["500 g"].UnitPriceSnapshot);
    }

    [Fact]
    public async Task Repricing_makes_the_next_checkout_go_through()
    {
        var data = InMemoryCommerceStore.CreateSeeded();
        var store = Harbor(data);
        var tomato = Tomato(data);
        var (cart, checkout) = Services(data, Fees);
        await Add(cart, store, tomato, 1);
        data.DefaultVariantOf(tomato).Price += 4;
        Assert.Equal(CheckoutOperationStatus.Conflict, (await checkout.CheckoutAsync(store.Id, Where, Key)).Status);

        await cart.RepriceAsync(store.Id);
        var placed = await checkout.CheckoutAsync(store.Id, Where, "attempt-0002-abcdef");

        Assert.Equal(CheckoutOperationStatus.Succeeded, placed.Status);
        Assert.Equal(tomato.Price + 4, placed.Order!.SubtotalAmount);
    }

    [Fact]
    public async Task Repricing_without_a_cart_is_not_found()
    {
        var data = InMemoryCommerceStore.CreateSeeded();
        var (cart, _) = Services(data, Fees);

        Assert.Equal(CartOperationStatus.NotFound, (await cart.RepriceAsync(Harbor(data).Id)).Status);
    }

    // ---------------- the order ----------------

    [Fact]
    public async Task The_order_carries_the_fee_breakdown_and_the_total_matches_the_cart_the_customer_saw()
    {
        var data = InMemoryCommerceStore.CreateSeeded();
        var store = Harbor(data);
        var tomato = Tomato(data);
        var (cart, checkout) = Services(data, Fees);
        await Add(cart, store, tomato, 2);
        var seen = (await cart.GetCartAsync(store.Id))!;

        var order = (await checkout.CheckoutAsync(store.Id, Where, Key)).Order!;

        Assert.Equal((seen.Subtotal, seen.DeliveryFee, seen.HandlingFee, seen.Total), (order.SubtotalAmount, order.DeliveryFee, order.HandlingFee, order.TotalAmount));
        Assert.Equal("CashOnDelivery", order.PaymentMethod);
        Assert.Null(order.ReceiverName);
        var stored = data.Orders.Single();
        Assert.Equal((seen.Subtotal, seen.DeliveryFee, seen.HandlingFee, seen.Total), (stored.SubtotalAmount, stored.DeliveryFee, stored.HandlingFee, stored.TotalAmount));
    }

    [Fact]
    public async Task A_big_enough_order_ships_free()
    {
        var data = InMemoryCommerceStore.CreateSeeded();
        var store = Harbor(data);
        var tomato = Tomato(data);
        var (cart, checkout) = Services(data, new PricingSettings { DeliveryFee = 25, HandlingFee = 5, FreeDeliveryThreshold = 10 });
        await Add(cart, store, tomato, 1);

        var order = (await checkout.CheckoutAsync(store.Id, Where, Key)).Order!;

        Assert.Equal((0m, 5m, tomato.Price + 5), (order.DeliveryFee, order.HandlingFee, order.TotalAmount));
    }

    [Fact]
    public async Task A_saved_address_supplies_the_text_the_point_and_the_receiver_and_later_edits_do_not_change_the_order()
    {
        var data = InMemoryCommerceStore.CreateSeeded();
        var store = Harbor(data);
        var customer = data.Customers.Single();
        var address = new CustomerAddress
        {
            CustomerId = customer.Id, Label = "Home", Line = "12 Marine Drive, Mumbai", FlatOrBuilding = "Flat 4", Landmark = "Near the station",
            Latitude = store.Latitude, Longitude = store.Longitude, ReceiverName = "Asha Patil", ReceiverPhone = "9876543210"
        };
        var (cart, checkout) = Services(data, Fees, address);
        await Add(cart, store, Tomato(data), 1);

        var order = (await checkout.CheckoutAsync(store.Id, new CheckoutRequest(AddressId: address.Id), Key)).Order!;
        address.Line = "Somewhere else entirely";
        address.ReceiverName = "Someone else";

        Assert.Equal("Flat 4, 12 Marine Drive, Mumbai (Landmark: Near the station)", order.DeliveryAddress);
        Assert.Equal((store.Latitude, store.Longitude), (order.Latitude, order.Longitude));
        Assert.Equal(("Asha Patil", "9876543210"), (order.ReceiverName, order.ReceiverPhone));
        var stored = data.Orders.Single();
        Assert.Equal(address.Id, stored.DeliveryAddressId);
        Assert.Equal("Flat 4, 12 Marine Drive, Mumbai (Landmark: Near the station)", stored.DeliveryAddress);
        Assert.Equal("Asha Patil", stored.ReceiverName);
    }

    [Fact]
    public async Task Someone_else_s_or_a_missing_address_is_not_found_and_nothing_is_taken()
    {
        var data = InMemoryCommerceStore.CreateSeeded();
        var store = Harbor(data);
        var tomato = Tomato(data);
        var theirs = new CustomerAddress { CustomerId = Guid.NewGuid(), Label = "Home", Line = "x", Latitude = store.Latitude, Longitude = store.Longitude, ReceiverName = "n", ReceiverPhone = "9876543210" };
        var (cart, checkout) = Services(data, Fees, theirs);
        await Add(cart, store, tomato, 1);
        var stock = data.StockOf(store, tomato).AvailableQuantity;

        var result = await checkout.CheckoutAsync(store.Id, new CheckoutRequest(AddressId: theirs.Id), Key);

        Assert.Equal(CheckoutOperationStatus.NotFound, result.Status);
        Assert.Equal(CheckoutOperationStatus.NotFound, (await checkout.CheckoutAsync(store.Id, new CheckoutRequest(AddressId: Guid.NewGuid()), Key)).Status);
        Assert.Equal(stock, data.StockOf(store, tomato).AvailableQuantity);
        Assert.Empty(data.Orders);
    }

    [Fact]
    public async Task A_saved_address_the_store_does_not_serve_is_refused_with_the_service_area_reason()
    {
        var data = InMemoryCommerceStore.CreateSeeded();
        var store = Harbor(data);
        var delhi = new CustomerAddress { CustomerId = data.Customers.Single().Id, Label = "Delhi", Line = "Connaught Place", Latitude = 28.61, Longitude = 77.21, ReceiverName = "n", ReceiverPhone = "9876543210" };
        var (cart, checkout) = Services(data, Fees, delhi);
        await Add(cart, store, Tomato(data), 1);

        var result = await checkout.CheckoutAsync(store.Id, new CheckoutRequest(AddressId: delhi.Id), Key);

        Assert.Equal((CheckoutOperationStatus.Conflict, ServiceabilityReasons.OutsideServiceArea), (result.Status, result.Reason));
    }

    [Fact]
    public async Task A_typed_address_still_needs_its_text_and_point()
    {
        var data = InMemoryCommerceStore.CreateSeeded();
        var (_, checkout) = Services(data, Fees);

        Assert.Equal(CheckoutOperationStatus.InvalidRequest, (await checkout.CheckoutAsync(Harbor(data).Id, new CheckoutRequest(), Key)).Status);
        Assert.Equal(CheckoutOperationStatus.InvalidRequest, (await checkout.CheckoutAsync(Harbor(data).Id, new CheckoutRequest("12 Main Street", null, 72.87), Key)).Status);
        Assert.Equal(CheckoutOperationStatus.InvalidRequest, (await checkout.CheckoutAsync(Harbor(data).Id, new CheckoutRequest("12 Main Street", 99, 72.87), Key)).Status);
    }

    // ---------------- failure reasons ----------------

    [Fact]
    public async Task An_empty_cart_has_its_own_reason()
    {
        var data = InMemoryCommerceStore.CreateSeeded();
        var store = Harbor(data);
        var (cart, checkout) = Services(data, Fees);
        await Add(cart, store, Tomato(data), 1);
        await cart.RemoveItemAsync(store.Id, Tomato(data).Id);

        var result = await checkout.CheckoutAsync(store.Id, Where, Key);

        Assert.Equal((CheckoutOperationStatus.Conflict, CheckoutReasons.CartEmpty), (result.Status, result.Reason));
    }

    [Fact]
    public async Task A_price_change_names_every_changed_line_with_the_old_and_new_price_and_changes_nothing()
    {
        var data = InMemoryCommerceStore.CreateSeeded();
        var store = Harbor(data);
        var tomato = Tomato(data);
        var half = AddVariant(data, tomato, "500 g", 15, 10, store);
        var (cart, checkout) = Services(data, Fees);
        await Add(cart, store, tomato, 1);
        await Add(cart, store, tomato, 2, half.Id);
        var oldPrice = tomato.Price;
        data.DefaultVariantOf(tomato).Price = oldPrice + 2;
        half.Price = 18;
        var stock = data.StockOf(store, tomato).AvailableQuantity;

        var result = await checkout.CheckoutAsync(store.Id, Where, Key);

        Assert.Equal(CheckoutReasons.PriceChanged, result.Reason);
        Assert.Equal(2, result.Details!.Count);
        var kilo = result.Details.Single(issue => issue.Label == "1 kg");
        Assert.Equal((oldPrice, oldPrice + 2, 1), (kilo.OldPrice, kilo.NewPrice, kilo.Quantity));
        var pack = result.Details.Single(issue => issue.Label == "500 g");
        Assert.Equal((15m, 18m, 2), (pack.OldPrice, pack.NewPrice, pack.Quantity));
        Assert.Equal(stock, data.StockOf(store, tomato).AvailableQuantity);
        Assert.Empty(data.Orders);
    }

    [Fact]
    public async Task Missing_stock_says_how_many_are_left_for_each_short_line_and_takes_nothing_from_the_others()
    {
        var data = InMemoryCommerceStore.CreateSeeded();
        var store = Harbor(data);
        var tomato = Tomato(data);
        var half = AddVariant(data, tomato, "500 g", 15, 10, store);
        var (cart, checkout) = Services(data, Fees);
        await Add(cart, store, tomato, 1);
        await Add(cart, store, tomato, 6, half.Id);
        data.Inventory.Single(row => row.VariantId == half.Id).AvailableQuantity = 2;
        var stock = data.StockOf(store, tomato).AvailableQuantity;

        var result = await checkout.CheckoutAsync(store.Id, Where, Key);

        Assert.Equal(CheckoutReasons.InventoryConflict, result.Reason);
        var issue = Assert.Single(result.Details!);
        Assert.Equal(("500 g", 6, 2), (issue.Label, issue.Quantity, issue.Available));
        Assert.Equal(stock, data.StockOf(store, tomato).AvailableQuantity);
    }

    [Fact]
    public async Task A_product_that_is_gone_is_named_and_comes_before_other_problems()
    {
        var data = InMemoryCommerceStore.CreateSeeded();
        var store = Harbor(data);
        var tomato = Tomato(data);
        var half = AddVariant(data, tomato, "500 g", 15, 10, store);
        var (cart, checkout) = Services(data, Fees);
        await Add(cart, store, tomato, 1);
        await Add(cart, store, tomato, 1, half.Id);
        half.IsActive = false;
        data.DefaultVariantOf(tomato).Price += 1;

        var result = await checkout.CheckoutAsync(store.Id, Where, Key);

        Assert.Equal(CheckoutReasons.ProductUnavailable, result.Reason);
        Assert.Equal("500 g", Assert.Single(result.Details!).Label);
    }

    // ---------------- the same order twice ----------------

    [Fact]
    public async Task The_same_key_returns_the_same_order_and_takes_stock_once()
    {
        var data = InMemoryCommerceStore.CreateSeeded();
        var store = Harbor(data);
        var tomato = Tomato(data);
        var (cart, checkout) = Services(data, Fees);
        await Add(cart, store, tomato, 2);
        var stock = data.StockOf(store, tomato).AvailableQuantity;

        var first = await checkout.CheckoutAsync(store.Id, Where, Key);
        var again = await checkout.CheckoutAsync(store.Id, Where, Key);

        Assert.False(first.Replayed);
        Assert.Equal(CheckoutOperationStatus.Succeeded, again.Status);
        Assert.True(again.Replayed);
        Assert.Equal(first.Order!.Id, again.Order!.Id);
        Assert.Equal(first.Order.OrderNumber, again.Order.OrderNumber);
        Assert.Single(data.Orders);
        Assert.Equal(stock - 2, data.StockOf(store, tomato).AvailableQuantity);
    }

    [Fact]
    public async Task A_new_key_after_the_first_order_finds_the_cart_empty_and_does_not_order_again()
    {
        var data = InMemoryCommerceStore.CreateSeeded();
        var store = Harbor(data);
        var (cart, checkout) = Services(data, Fees);
        await Add(cart, store, Tomato(data), 1);
        await checkout.CheckoutAsync(store.Id, Where, Key);

        var second = await checkout.CheckoutAsync(store.Id, Where, "attempt-0002-abcdef");

        Assert.Equal((CheckoutOperationStatus.Conflict, CheckoutReasons.CartEmpty), (second.Status, second.Reason));
        Assert.Single(data.Orders);
    }

    [Fact]
    public async Task The_same_key_for_another_place_is_refused_and_nothing_changes()
    {
        var data = InMemoryCommerceStore.CreateSeeded();
        var store = Harbor(data);
        var (cart, checkout) = Services(data, Fees);
        await Add(cart, store, Tomato(data), 1);
        await checkout.CheckoutAsync(store.Id, Where, Key);
        await Add(cart, store, Tomato(data), 1);

        var other = await checkout.CheckoutAsync(store.Id, new CheckoutRequest("40 Other Road", 19.08, 72.88), Key);

        Assert.Equal((CheckoutOperationStatus.Conflict, CheckoutReasons.IdempotencyKeyReused), (other.Status, other.Reason));
        Assert.Single(data.Orders);
        Assert.Single((await cart.GetCartAsync(store.Id))!.Items);
    }

    [Fact]
    public async Task Without_a_key_nothing_is_remembered()
    {
        var data = InMemoryCommerceStore.CreateSeeded();
        var store = Harbor(data);
        var (cart, checkout) = Services(data, Fees);
        await Add(cart, store, Tomato(data), 1);

        var first = await checkout.CheckoutAsync(store.Id, Where);
        await Add(cart, store, Tomato(data), 1);
        var second = await checkout.CheckoutAsync(store.Id, Where);

        Assert.Equal(CheckoutOperationStatus.Succeeded, first.Status);
        Assert.Equal(CheckoutOperationStatus.Succeeded, second.Status);
        Assert.False(second.Replayed);
        Assert.Equal(2, data.Orders.Count);
        Assert.Empty(data.CheckoutRequests);
    }

    [Fact]
    public async Task A_key_is_forgotten_after_a_day()
    {
        var data = InMemoryCommerceStore.CreateSeeded();
        var store = Harbor(data);
        var (cart, checkout) = Services(data, Fees);
        await Add(cart, store, Tomato(data), 1);
        var first = await checkout.CheckoutAsync(store.Id, Where, Key);
        data.CheckoutRequests.Single().CreatedAt = DateTime.UtcNow.AddHours(-25);
        await Add(cart, store, Tomato(data), 1);

        var later = await checkout.CheckoutAsync(store.Id, Where, Key);

        Assert.Equal(CheckoutOperationStatus.Succeeded, later.Status);
        Assert.False(later.Replayed);
        Assert.NotEqual(first.Order!.Id, later.Order!.Id);
    }

    [Theory]
    [InlineData("short")]
    [InlineData("has spaces in it ok")]
    [InlineData("semi;colon-key-12345")]
    public async Task A_badly_formed_key_is_refused_before_anything_happens(string key)
    {
        var data = InMemoryCommerceStore.CreateSeeded();
        var store = Harbor(data);
        var (cart, checkout) = Services(data, Fees);
        await Add(cart, store, Tomato(data), 1);

        var result = await checkout.CheckoutAsync(store.Id, Where, key);

        Assert.Equal(CheckoutOperationStatus.InvalidRequest, result.Status);
        Assert.Empty(data.Orders);
    }

    [Fact]
    public async Task Parallel_taps_with_one_key_make_one_order()
    {
        var data = InMemoryCommerceStore.CreateSeeded();
        var store = Harbor(data);
        var (cart, checkout) = Services(data, Fees);
        await Add(cart, store, Tomato(data), 1);

        var taps = await Task.WhenAll(Enumerable.Range(0, 8).Select(_ => Task.Run(() => checkout.CheckoutAsync(store.Id, Where, Key))));

        Assert.All(taps, tap => Assert.Equal(CheckoutOperationStatus.Succeeded, tap.Status));
        Assert.Single(taps.Select(tap => tap.Order!.Id).Distinct());
        Assert.Single(data.Orders);
        Assert.Equal(1, taps.Count(tap => !tap.Replayed));
    }

    // ---------------- the routes ----------------

    private static object? Field(object body, string name) => body.GetType().GetProperty(name)!.GetValue(body);

    [Fact]
    public async Task The_checkout_route_answers_201_the_first_time_and_200_with_the_same_order_after()
    {
        var data = InMemoryCommerceStore.CreateSeeded();
        var store = Harbor(data);
        var (cart, checkout) = Services(data, Fees);
        await Add(cart, store, Tomato(data), 1);
        var controller = new CheckoutController(checkout) { ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext() } };
        controller.HttpContext.Request.Headers["Idempotency-Key"] = Key;

        var first = Assert.IsType<CreatedResult>(await controller.Checkout(store.Id, Where, CancellationToken.None));
        var second = Assert.IsType<OkObjectResult>(await controller.Checkout(store.Id, Where, CancellationToken.None));

        Assert.Equal(true, Field(second.Value!, "replayed"));
        Assert.Equal(((OrderResponse)Field(first.Value!, "data")!).Id, ((OrderResponse)Field(second.Value!, "data")!).Id);
    }

    [Fact]
    public async Task The_checkout_route_returns_the_reason_and_the_lines_involved()
    {
        var data = InMemoryCommerceStore.CreateSeeded();
        var store = Harbor(data);
        var tomato = Tomato(data);
        var (cart, checkout) = Services(data, Fees);
        await Add(cart, store, tomato, 1);
        data.DefaultVariantOf(tomato).Price += 2;
        var controller = new CheckoutController(checkout) { ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext() } };

        var conflict = Assert.IsType<ConflictObjectResult>(await controller.Checkout(store.Id, Where, CancellationToken.None));

        Assert.Equal(CheckoutReasons.PriceChanged, Field(conflict.Value!, "reason"));
        Assert.Single((IReadOnlyList<CheckoutIssue>)Field(conflict.Value!, "details")!);
    }

    [Fact]
    public async Task The_cart_routes_answer_204_for_no_cart_and_return_merge_notes()
    {
        var data = InMemoryCommerceStore.CreateSeeded();
        var store = Harbor(data);
        var (cart, _) = Services(data, Fees);
        var controller = new CartController(cart);

        Assert.IsType<NoContentResult>(await controller.Current(CancellationToken.None));
        var merged = Assert.IsType<OkObjectResult>(await controller.Merge(store.Id, new MergeCartRequest([new AddCartItemRequest(Guid.NewGuid(), 1), new AddCartItemRequest(Tomato(data).Id, 1)]), CancellationToken.None));

        var mergedData = Field(merged.Value!, "data")!;
        Assert.Single((IReadOnlyList<CartNote>)Field(mergedData, "notes")!);
        Assert.Single(((CartResponse)Field(mergedData, "cart")!).Items);
        Assert.IsType<OkObjectResult>(await controller.Current(CancellationToken.None));
        Assert.IsType<OkObjectResult>(await controller.Clear(store.Id, CancellationToken.None));
        Assert.IsType<NoContentResult>(await controller.Current(CancellationToken.None));
    }

    [Fact]
    public void The_pricing_route_publishes_the_fee_settings_for_guests()
    {
        var response = Assert.IsType<OkObjectResult>(new CatalogController(null!, null!, InMemoryCommerceStore.CreateSeeded(), null!, Fees).Pricing());

        var data = Field(response.Value!, "data")!;
        Assert.Equal((25m, 5m, 199m), ((decimal)Field(data, "deliveryFee")!, (decimal)Field(data, "handlingFee")!, (decimal)Field(data, "freeDeliveryThreshold")!));
    }
}
