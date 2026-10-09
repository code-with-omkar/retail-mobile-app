using System.Security.Claims;
using System.Text.Json;
using Microsoft.AspNetCore.Http;
using QuickCommerce.Api.Security;
using QuickCommerce.Application;
using QuickCommerce.Application.DTOs;
using QuickCommerce.Application.Interfaces;
using QuickCommerce.Application.Services;
using QuickCommerce.Application.Validators;
using QuickCommerce.Domain;
using QuickCommerce.Infrastructure;
using QuickCommerce.Infrastructure.Security;
using Xunit;

namespace QuickCommerce.Tests;

/// <summary>Phase P3: pack sizes. Catalogue, cart, checkout and admin orders, and compatibility with clients that know nothing about variants.</summary>
public sealed class ProductVariantTests
{
    private static Store Harbor(InMemoryCommerceStore data) => data.Stores.Single(store => store.Name == "Harbor Point Dark Store");
    private static Product Tomato(InMemoryCommerceStore data) => data.Products.Single(product => product.Name == "Tomato");

    /// <summary>Adds a pack size to a product and stocks it in the given stores.</summary>
    private static ProductVariant AddVariant(InMemoryCommerceStore data, Product product, string label, decimal price, decimal? mrp, int sortOrder, int stock, params Store[] stores)
    {
        var variant = new ProductVariant { ProductId = product.Id, Sku = $"{product.Sku}-{label.Replace(" ", string.Empty)}", Label = label, Price = price, Mrp = mrp, SortOrder = sortOrder };
        data.Variants.Add(variant);
        foreach (var store in stores)
        {
            data.Inventory.Add(new StoreVariantInventory { StoreId = store.Id, VariantId = variant.Id, AvailableQuantity = stock });
        }

        return variant;
    }

    private static CatalogService Catalogue(InMemoryCommerceStore data) => new(data, new StoreSelectionService(), new NoCache(), new DeliveryEstimator(new DeliverySettings()));

    private static (CartService Cart, CheckoutService Checkout) CartAndCheckout(InMemoryCommerceStore data)
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
        return (new CartService(data, resolver, new AddCartItemRequestValidator(), new UpdateCartItemRequestValidator()), new CheckoutService(data, resolver, new CheckoutRequestValidator()));
    }

    private static CheckoutRequest Address => new("12 Main Street", 19.07, 72.87);

    // ---------------- catalogue ----------------

    [Fact]
    public async Task A_product_lists_its_variants_with_their_own_price_mrp_and_discount()
    {
        var data = InMemoryCommerceStore.CreateSeeded();
        var tomato = Tomato(data);
        data.DefaultVariantOf(tomato).Mrp = 50;
        AddVariant(data, tomato, "500 g", 25, 28, 1, 10);
        AddVariant(data, tomato, "250 g", 13, null, 2, 10);

        var product = (await Catalogue(data).GetCatalogProductAsync(tomato.Id))!;

        Assert.Equal(["1 kg", "500 g", "250 g"], product.Variants!.Select(variant => variant.Label));
        var (kilo, half, quarter) = (product.Variants![0], product.Variants[1], product.Variants[2]);
        Assert.Equal((tomato.Price, 50m, true), (kilo.Price, kilo.Mrp, kilo.IsDefault));
        Assert.Equal((25m, 28m, 11, false), (half.Price, half.Mrp, half.DiscountPercent, half.IsDefault));
        Assert.Equal((13m, 13m, 0), (quarter.Price, quarter.Mrp, quarter.DiscountPercent));
        Assert.Single(product.Variants, variant => variant.IsDefault);
    }

    [Fact]
    public async Task The_products_own_fields_describe_its_default_variant_so_old_clients_see_what_they_always_saw()
    {
        var data = InMemoryCommerceStore.CreateSeeded();
        var tomato = Tomato(data);
        var @default = data.DefaultVariantOf(tomato);
        @default.Price = 31;
        @default.Mrp = 40;
        @default.Label = "1 kg pack";
        AddVariant(data, tomato, "500 g", 17, null, 1, 10);

        var product = (await Catalogue(data).GetCatalogProductAsync(tomato.Id))!;

        Assert.Equal((31m, 40m, 23, "1 kg pack"), (product.Price, product.Mrp, product.DiscountPercent, product.UnitOfMeasure));
    }

    [Fact]
    public async Task Inactive_variants_are_hidden_and_a_product_without_any_falls_back_to_its_own_fields()
    {
        var data = InMemoryCommerceStore.CreateSeeded();
        var tomato = Tomato(data);
        AddVariant(data, tomato, "500 g", 25, null, 1, 10).IsActive = false;
        var potato = data.Products.Single(product => product.Name == "Potato");
        data.Variants.RemoveAll(variant => variant.ProductId == potato.Id);

        var tomatoResponse = (await Catalogue(data).GetCatalogProductAsync(tomato.Id))!;
        var potatoResponse = (await Catalogue(data).GetCatalogProductAsync(potato.Id))!;

        Assert.Single(tomatoResponse.Variants!);
        Assert.Empty(potatoResponse.Variants!);
        Assert.Equal((potato.Price, potato.UnitOfMeasure), (potatoResponse.Price, potatoResponse.UnitOfMeasure));
    }

    [Fact]
    public async Task Stock_flags_are_per_variant_and_the_products_flags_follow_the_default()
    {
        var data = InMemoryCommerceStore.CreateSeeded();
        var harbor = Harbor(data);
        var tomato = Tomato(data);
        data.StockOf(harbor, tomato).AvailableQuantity = 0; // default pack sold out
        var half = AddVariant(data, tomato, "500 g", 25, null, 1, 20, harbor);
        var quarter = AddVariant(data, tomato, "250 g", 13, null, 2, 0, harbor);
        var carton = AddVariant(data, tomato, "5 kg", 120, null, 3, 3, harbor);
        data.Inventory.Single(row => row.StoreId == harbor.Id && row.VariantId == carton.Id).ReorderThreshold = 5;

        var result = await Catalogue(data).GetCatalogProductsAsync(new CatalogProductQuery("Tomato", null, 1, 20, harbor.Id));
        var product = result.Page!.Items.Single();
        var flags = product.Variants!.ToDictionary(variant => variant.Label, variant => (variant.InStock, variant.LowStock));

        Assert.Equal((false, false), flags["1 kg"]);
        Assert.Equal((true, false), flags["500 g"]);
        Assert.Equal((false, false), flags["250 g"]);
        Assert.Equal((true, true), flags["5 kg"]);
        Assert.Equal((false, false), (product.InStock, product.LowStock));
        Assert.DoesNotContain("Quantity", string.Join(",", typeof(CatalogVariantResponse).GetProperties().Select(property => property.Name)));
        _ = half;
        _ = quarter;
    }

    [Fact]
    public async Task Without_a_store_variant_stock_is_unknown()
    {
        var data = InMemoryCommerceStore.CreateSeeded();
        AddVariant(data, Tomato(data), "500 g", 25, null, 1, 5, Harbor(data));

        var product = (await Catalogue(data).GetCatalogProductAsync(Tomato(data).Id))!;

        Assert.All(product.Variants!, variant => Assert.Null(variant.InStock));
        Assert.Null(product.InStock);
    }

    [Fact]
    public async Task A_store_carries_a_product_when_it_stocks_any_active_variant_of_it()
    {
        var data = InMemoryCommerceStore.CreateSeeded();
        var cedar = data.Stores.Single(store => store.Name == "Cedar Market Hub");
        var tomato = Tomato(data);
        data.Inventory.RemoveAll(row => row.StoreId == cedar.Id && row.VariantId == data.DefaultVariantOf(tomato).Id);
        var service = Catalogue(data);
        Assert.DoesNotContain((await service.GetCatalogProductsAsync(new CatalogProductQuery(null, null, 1, 50, cedar.Id, CarriedOnly: true))).Page!.Items, item => item.Id == tomato.Id);

        AddVariant(data, tomato, "500 g", 25, null, 1, 5, cedar);

        Assert.Contains((await service.GetCatalogProductsAsync(new CatalogProductQuery(null, null, 1, 50, cedar.Id, CarriedOnly: true))).Page!.Items, item => item.Id == tomato.Id);
    }

    // ---------------- cart ----------------

    [Fact]
    public async Task Adding_a_variant_snapshots_its_label_and_price_and_two_sizes_are_two_lines()
    {
        var data = InMemoryCommerceStore.CreateSeeded();
        var store = Harbor(data);
        var tomato = Tomato(data);
        var half = AddVariant(data, tomato, "500 g", 25, 28, 1, 10, store);
        var (cart, _) = CartAndCheckout(data);

        await cart.AddItemAsync(store.Id, new AddCartItemRequest(tomato.Id, 1, half.Id));
        var result = await cart.AddItemAsync(store.Id, new AddCartItemRequest(tomato.Id, 2));

        Assert.Equal(CartOperationStatus.Succeeded, result.Status);
        var lines = result.Cart!.Items.ToDictionary(item => item.VariantLabel!);
        Assert.Equal(2, lines.Count);
        Assert.Equal((25m, 1, half.Id), (lines["500 g"].UnitPriceSnapshot, lines["500 g"].Quantity, lines["500 g"].VariantId));
        Assert.Equal((tomato.Price, 2, data.DefaultVariantOf(tomato).Id), (lines["1 kg"].UnitPriceSnapshot, lines["1 kg"].Quantity, lines["1 kg"].VariantId));
        Assert.Equal(25m + tomato.Price * 2, result.Cart.TotalAmount);
    }

    [Fact]
    public async Task Adding_the_same_variant_again_increases_its_line()
    {
        var data = InMemoryCommerceStore.CreateSeeded();
        var store = Harbor(data);
        var half = AddVariant(data, Tomato(data), "500 g", 25, null, 1, 10, store);
        var (cart, _) = CartAndCheckout(data);

        await cart.AddItemAsync(store.Id, new AddCartItemRequest(Tomato(data).Id, 1, half.Id));
        var result = await cart.AddItemAsync(store.Id, new AddCartItemRequest(Tomato(data).Id, 3, half.Id));

        Assert.Equal(4, result.Cart!.Items.Single().Quantity);
    }

    [Fact]
    public async Task A_request_without_a_variant_uses_the_default_variant()
    {
        var data = InMemoryCommerceStore.CreateSeeded();
        var store = Harbor(data);
        var tomato = Tomato(data);
        data.DefaultVariantOf(tomato).Price = 33;
        AddVariant(data, tomato, "500 g", 17, null, 1, 10, store);
        var (cart, _) = CartAndCheckout(data);

        var result = await cart.AddItemAsync(store.Id, new AddCartItemRequest(tomato.Id, 1));

        var line = result.Cart!.Items.Single();
        Assert.Equal((33m, data.DefaultVariantOf(tomato).Id), (line.UnitPriceSnapshot, line.VariantId));
    }

    [Fact]
    public async Task A_variant_of_another_product_or_an_inactive_one_is_rejected()
    {
        var data = InMemoryCommerceStore.CreateSeeded();
        var store = Harbor(data);
        var tomato = Tomato(data);
        var potato = data.Products.Single(product => product.Name == "Potato");
        var inactive = AddVariant(data, tomato, "500 g", 25, null, 1, 10, store);
        inactive.IsActive = false;
        var (cart, _) = CartAndCheckout(data);

        var wrongProduct = await cart.AddItemAsync(store.Id, new AddCartItemRequest(tomato.Id, 1, data.DefaultVariantOf(potato).Id));
        var unknown = await cart.AddItemAsync(store.Id, new AddCartItemRequest(tomato.Id, 1, Guid.NewGuid()));
        var inactiveResult = await cart.AddItemAsync(store.Id, new AddCartItemRequest(tomato.Id, 1, inactive.Id));
        var empty = await cart.AddItemAsync(store.Id, new AddCartItemRequest(tomato.Id, 1, Guid.Empty));

        Assert.Equal(CartOperationStatus.NotFound, wrongProduct.Status);
        Assert.Equal(CartOperationStatus.NotFound, unknown.Status);
        Assert.Equal(CartOperationStatus.NotFound, inactiveResult.Status);
        Assert.Equal(CartOperationStatus.InvalidRequest, empty.Status);
        Assert.Empty(data.Carts);
    }

    [Fact]
    public async Task Update_and_remove_address_a_variant_line_and_fall_back_to_the_only_line_or_the_default()
    {
        var data = InMemoryCommerceStore.CreateSeeded();
        var store = Harbor(data);
        var tomato = Tomato(data);
        var half = AddVariant(data, tomato, "500 g", 25, null, 1, 10, store);
        var (cart, _) = CartAndCheckout(data);
        await cart.AddItemAsync(store.Id, new AddCartItemRequest(tomato.Id, 1, half.Id));

        // One line of the product: an old client naming only the product reaches it.
        var viaProduct = await cart.UpdateItemAsync(store.Id, tomato.Id, new UpdateCartItemRequest(4));
        Assert.Equal(4, viaProduct.Cart!.Items.Single().Quantity);

        // Two lines: naming the variant reaches that one; naming only the product reaches the default variant's.
        await cart.AddItemAsync(store.Id, new AddCartItemRequest(tomato.Id, 1));
        var viaVariant = await cart.UpdateItemAsync(store.Id, tomato.Id, new UpdateCartItemRequest(7), half.Id);
        Assert.Equal(7, viaVariant.Cart!.Items.Single(item => item.VariantId == half.Id).Quantity);
        var viaDefault = await cart.UpdateItemAsync(store.Id, tomato.Id, new UpdateCartItemRequest(9));
        Assert.Equal(9, viaDefault.Cart!.Items.Single(item => item.VariantId == data.DefaultVariantOf(tomato).Id).Quantity);
        Assert.Equal(7, viaDefault.Cart.Items.Single(item => item.VariantId == half.Id).Quantity);

        var removed = await cart.RemoveItemAsync(store.Id, tomato.Id, half.Id);
        Assert.Equal([data.DefaultVariantOf(tomato).Id], removed.Cart!.Items.Select(item => item.VariantId!.Value));
        var missing = await cart.RemoveItemAsync(store.Id, tomato.Id, half.Id);
        Assert.Equal(CartOperationStatus.NotFound, missing.Status);
    }

    // ---------------- checkout ----------------

    [Fact]
    public async Task Checkout_takes_stock_from_each_variant_and_snapshots_label_price_and_mrp()
    {
        var data = InMemoryCommerceStore.CreateSeeded();
        var store = Harbor(data);
        var tomato = Tomato(data);
        var half = AddVariant(data, tomato, "500 g", 25, 28, 1, 10, store);
        var kiloStock = data.StockOf(store, tomato).AvailableQuantity;
        var (cart, checkout) = CartAndCheckout(data);
        await cart.AddItemAsync(store.Id, new AddCartItemRequest(tomato.Id, 2, half.Id));
        await cart.AddItemAsync(store.Id, new AddCartItemRequest(tomato.Id, 1));

        var result = await checkout.CheckoutAsync(store.Id, Address);

        Assert.Equal(CheckoutOperationStatus.Succeeded, result.Status);
        Assert.Equal(kiloStock - 1, data.StockOf(store, tomato).AvailableQuantity);
        Assert.Equal(8, data.Inventory.Single(row => row.StoreId == store.Id && row.VariantId == half.Id).AvailableQuantity);
        var lines = data.Orders.Single().Items.ToDictionary(item => item.VariantLabelSnapshot!);
        Assert.Equal((25m, 28m, half.Id, 2), (lines["500 g"].UnitPrice, lines["500 g"].UnitMrpSnapshot!.Value, lines["500 g"].VariantId!.Value, lines["500 g"].Quantity));
        Assert.Null(lines["1 kg"].UnitMrpSnapshot);
        Assert.Equal(25m * 2 + tomato.Price, result.Order!.TotalAmount);
        Assert.Contains(result.Order.Items, item => item.VariantLabel == "500 g" && item.VariantId == half.Id);
    }

    [Fact]
    public async Task A_price_change_on_the_variant_in_the_cart_stops_checkout_and_changes_nothing()
    {
        var data = InMemoryCommerceStore.CreateSeeded();
        var store = Harbor(data);
        var tomato = Tomato(data);
        var half = AddVariant(data, tomato, "500 g", 25, null, 1, 10, store);
        var (cart, checkout) = CartAndCheckout(data);
        await cart.AddItemAsync(store.Id, new AddCartItemRequest(tomato.Id, 1, half.Id));
        half.Price = 26;

        var result = await checkout.CheckoutAsync(store.Id, Address);

        Assert.Equal(CheckoutOperationStatus.Conflict, result.Status);
        Assert.Equal(10, data.Inventory.Single(row => row.StoreId == store.Id && row.VariantId == half.Id).AvailableQuantity);
        Assert.Empty(data.Orders);
        Assert.Single(data.Carts.Single().Items);
    }

    [Fact]
    public async Task A_price_change_on_another_variant_of_the_product_does_not_matter()
    {
        var data = InMemoryCommerceStore.CreateSeeded();
        var store = Harbor(data);
        var tomato = Tomato(data);
        var half = AddVariant(data, tomato, "500 g", 25, null, 1, 10, store);
        var (cart, checkout) = CartAndCheckout(data);
        await cart.AddItemAsync(store.Id, new AddCartItemRequest(tomato.Id, 1, half.Id));
        data.DefaultVariantOf(tomato).Price += 5;

        Assert.Equal(CheckoutOperationStatus.Succeeded, (await checkout.CheckoutAsync(store.Id, Address)).Status);
    }

    [Fact]
    public async Task A_sold_out_variant_blocks_checkout_even_when_the_default_pack_is_in_stock()
    {
        var data = InMemoryCommerceStore.CreateSeeded();
        var store = Harbor(data);
        var tomato = Tomato(data);
        var half = AddVariant(data, tomato, "500 g", 25, null, 1, 1, store);
        var (cart, checkout) = CartAndCheckout(data);
        await cart.AddItemAsync(store.Id, new AddCartItemRequest(tomato.Id, 2, half.Id));

        var result = await checkout.CheckoutAsync(store.Id, Address);

        Assert.Equal(CheckoutOperationStatus.Conflict, result.Status);
        Assert.Equal(1, data.Inventory.Single(row => row.StoreId == store.Id && row.VariantId == half.Id).AvailableQuantity);
        Assert.Empty(data.Orders);
    }

    [Fact]
    public async Task A_variant_deactivated_after_it_was_added_blocks_checkout()
    {
        var data = InMemoryCommerceStore.CreateSeeded();
        var store = Harbor(data);
        var tomato = Tomato(data);
        var half = AddVariant(data, tomato, "500 g", 25, null, 1, 10, store);
        var (cart, checkout) = CartAndCheckout(data);
        await cart.AddItemAsync(store.Id, new AddCartItemRequest(tomato.Id, 1, half.Id));
        half.IsActive = false;

        Assert.Equal(CheckoutOperationStatus.Conflict, (await checkout.CheckoutAsync(store.Id, Address)).Status);
        Assert.Empty(data.Orders);
    }

    [Fact]
    public async Task A_placed_order_keeps_its_price_and_label_when_the_variant_changes_later()
    {
        var data = InMemoryCommerceStore.CreateSeeded();
        var store = Harbor(data);
        var tomato = Tomato(data);
        var half = AddVariant(data, tomato, "500 g", 25, 28, 1, 10, store);
        var (cart, checkout) = CartAndCheckout(data);
        await cart.AddItemAsync(store.Id, new AddCartItemRequest(tomato.Id, 2, half.Id));
        var placed = await checkout.CheckoutAsync(store.Id, Address);

        half.Price = 99;
        half.Mrp = 120;
        half.Label = "half kilo";

        var line = data.Orders.Single().Items.Single();
        Assert.Equal((25m, 28m, "500 g"), (line.UnitPrice, line.UnitMrpSnapshot!.Value, line.VariantLabelSnapshot));
        Assert.Equal(50m, data.Orders.Single().TotalAmount);
        Assert.Equal(50m, placed.Order!.TotalAmount);
    }

    // ---------------- admin order creation ----------------

    [Fact]
    public async Task An_admin_order_can_name_a_variant_or_use_the_default_and_decrements_that_variants_stock()
    {
        var data = InMemoryCommerceStore.CreateSeeded();
        var harbor = Harbor(data);
        var tomato = Tomato(data);
        var half = AddVariant(data, tomato, "500 g", 25, null, 1, 10, harbor);
        var kiloStock = data.StockOf(harbor, tomato).AvailableQuantity;
        var service = new OrderService(data, new StoreSelectionService(), new CreateOrderRequestValidator());

        var result = await service.CreateOrderAsync(new CreateOrderRequest(Guid.NewGuid(), harbor.Latitude, harbor.Longitude, "12 Marine Drive",
            [new OrderLineRequest(tomato.Id, 2, half.Id), new OrderLineRequest(tomato.Id, 1)]));

        Assert.Equal(CreateOrderStatus.Created, result.Status);
        Assert.Equal(8, data.Inventory.Single(row => row.StoreId == harbor.Id && row.VariantId == half.Id).AvailableQuantity);
        Assert.Equal(kiloStock - 1, data.StockOf(harbor, tomato).AvailableQuantity);
        Assert.Contains(result.Order!.Items, item => item.VariantLabel == "500 g" && item.UnitPrice == 25m && item.Quantity == 2);
        Assert.Equal(50m + tomato.Price, result.Order.TotalAmount);
    }

    [Fact]
    public async Task An_admin_order_with_a_variant_of_another_product_is_invalid_and_changes_no_stock()
    {
        var data = InMemoryCommerceStore.CreateSeeded();
        var harbor = Harbor(data);
        var potato = data.Products.Single(product => product.Name == "Potato");
        var before = data.Inventory.Select(row => row.AvailableQuantity).ToArray();
        var service = new OrderService(data, new StoreSelectionService(), new CreateOrderRequestValidator());

        var result = await service.CreateOrderAsync(new CreateOrderRequest(Guid.NewGuid(), harbor.Latitude, harbor.Longitude, "12 Marine Drive",
            [new OrderLineRequest(Tomato(data).Id, 1, data.DefaultVariantOf(potato).Id)]));

        Assert.Equal(CreateOrderStatus.InvalidRequest, result.Status);
        Assert.Equal(before, data.Inventory.Select(row => row.AvailableQuantity).ToArray());
        Assert.Empty(data.Orders);
    }

    [Fact]
    public async Task The_store_for_an_admin_order_must_stock_every_named_variant()
    {
        var data = InMemoryCommerceStore.CreateSeeded();
        var harbor = Harbor(data);
        var tomato = Tomato(data);
        var rare = AddVariant(data, tomato, "5 kg", 120, null, 1, 5, data.Stores.Single(store => store.Name == "North Star Fulfillment"));
        var service = new OrderService(data, new StoreSelectionService(), new CreateOrderRequestValidator());

        var result = await service.CreateOrderAsync(new CreateOrderRequest(Guid.NewGuid(), harbor.Latitude, harbor.Longitude, "12 Marine Drive", [new OrderLineRequest(tomato.Id, 1, rare.Id)]));

        Assert.Equal(CreateOrderStatus.Created, result.Status);
        Assert.Equal(data.Stores.Single(store => store.Name == "North Star Fulfillment").Id, result.Order!.StoreId);
    }

    // ---------------- compatibility ----------------

    [Fact]
    public void Old_request_bodies_without_a_variant_still_deserialize()
    {
        var options = new JsonSerializerOptions(JsonSerializerDefaults.Web);
        var cart = JsonSerializer.Deserialize<AddCartItemRequest>("""{"productId":"7d1b2b6a-7e4b-4f0e-9d0c-3a1f2d6f4b11","quantity":2}""", options)!;
        var line = JsonSerializer.Deserialize<OrderLineRequest>("""{"productId":"7d1b2b6a-7e4b-4f0e-9d0c-3a1f2d6f4b11","quantity":1}""", options)!;

        Assert.Equal((2, null), (cart.Quantity, cart.VariantId));
        Assert.Null(line.VariantId);
    }

    [Fact]
    public void Existing_response_fields_are_all_still_there()
    {
        var product = typeof(CatalogProductResponse).GetProperties().Select(property => property.Name).ToHashSet();
        Assert.True(new[] { "Id", "Name", "Description", "Price", "Mrp", "DiscountPercent", "UnitOfMeasure", "CategoryId", "ImageUrl", "Translations", "InStock", "LowStock" }.All(product.Contains));
        var cartItem = typeof(CartItemResponse).GetProperties().Select(property => property.Name).ToHashSet();
        Assert.True(new[] { "ProductId", "ProductNameSnapshot", "UnitPriceSnapshot", "Quantity", "TotalPrice" }.All(cartItem.Contains));
        var orderItem = typeof(OrderItemResponse).GetProperties().Select(property => property.Name).ToHashSet();
        Assert.True(new[] { "ProductId", "ProductNameSnapshot", "UnitPrice", "Quantity", "TotalPrice" }.All(orderItem.Contains));
    }

    [Fact]
    public void Customer_variant_responses_never_carry_a_quantity()
    {
        var names = typeof(CatalogVariantResponse).GetProperties().Select(property => property.Name).Order().ToArray();
        Assert.Equal(["DiscountPercent", "Id", "InStock", "IsDefault", "Label", "LowStock", "Mrp", "Price"], names);
    }

    private sealed class NoCache : ICacheService
    {
        public Task<T?> GetAsync<T>(string key, CancellationToken cancellationToken = default) => Task.FromResult<T?>(default);
        public Task SetAsync<T>(string key, T value, TimeSpan ttl, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task RemoveAsync(string key, CancellationToken cancellationToken = default) => Task.CompletedTask;
    }
}
