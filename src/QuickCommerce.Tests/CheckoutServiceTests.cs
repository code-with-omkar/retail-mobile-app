using System.Security.Claims;
using Microsoft.AspNetCore.Http;
using QuickCommerce.Api.Security;
using QuickCommerce.Application.DTOs;
using QuickCommerce.Application.Services;
using QuickCommerce.Application.Validators;
using QuickCommerce.Infrastructure;
using QuickCommerce.Infrastructure.Security;
using Xunit;

namespace QuickCommerce.Tests;

public sealed class CheckoutServiceTests
{
    [Fact]
    public async Task Checkout_creates_order_deducts_inventory_and_clears_cart()
    {
        var data = InMemoryCommerceStore.CreateSeeded();
        var (cartService, checkoutService) = CreateServices(data);
        var store = data.Stores.First();
        var product = data.Products.First();
        var inventory = data.StockOf(store, product);
        var initialQuantity = inventory.AvailableQuantity;

        await cartService.AddItemAsync(store.Id, new AddCartItemRequest(product.Id, 2));
        var result = await checkoutService.CheckoutAsync(store.Id, new CheckoutRequest("12 Main Street", 19.07, 72.87));

        Assert.Equal(CheckoutOperationStatus.Succeeded, result.Status);
        Assert.NotNull(result.Order);
        Assert.Equal(data.Users.Single().Id, result.Order!.UserId);
        Assert.Equal(product.Price * 2, result.Order.TotalAmount);
        Assert.Equal(initialQuantity - 2, inventory.AvailableQuantity);
        Assert.Empty(data.Carts.Single().Items);
        Assert.Single(data.Orders);
    }

    [Fact]
    public async Task Checkout_rejects_price_change_without_mutating_cart_inventory_or_orders()
    {
        var data = InMemoryCommerceStore.CreateSeeded();
        var (cartService, checkoutService) = CreateServices(data);
        var store = data.Stores.First();
        var product = data.Products.First();
        var inventory = data.StockOf(store, product);
        var initialQuantity = inventory.AvailableQuantity;

        await cartService.AddItemAsync(store.Id, new AddCartItemRequest(product.Id, 1));
        data.DefaultVariantOf(product).Price += 1;
        var result = await checkoutService.CheckoutAsync(store.Id, new CheckoutRequest("12 Main Street", 19.07, 72.87));

        Assert.Equal(CheckoutOperationStatus.Conflict, result.Status);
        Assert.Equal(initialQuantity, inventory.AvailableQuantity);
        Assert.Empty(data.Orders);
        Assert.Single(data.Carts.Single().Items);
    }

    [Fact]
    public async Task Checkout_rolls_back_when_inventory_is_insufficient()
    {
        var data = InMemoryCommerceStore.CreateSeeded();
        var (cartService, checkoutService) = CreateServices(data);
        var store = data.Stores.First();
        var product = data.Products.First();
        var inventory = data.StockOf(store, product);
        var initialQuantity = inventory.AvailableQuantity;

        await cartService.AddItemAsync(store.Id, new AddCartItemRequest(product.Id, initialQuantity + 1));
        var result = await checkoutService.CheckoutAsync(store.Id, new CheckoutRequest("12 Main Street", 19.07, 72.87));

        Assert.Equal(CheckoutOperationStatus.Conflict, result.Status);
        Assert.Equal(initialQuantity, inventory.AvailableQuantity);
        Assert.Empty(data.Orders);
        Assert.Equal(initialQuantity + 1, Assert.Single(data.Carts.Single().Items).Quantity);
    }

    [Fact]
    public async Task Checkout_rejects_empty_cart_and_invalid_request()
    {
        var data = InMemoryCommerceStore.CreateSeeded();
        var (_, checkoutService) = CreateServices(data);
        var store = data.Stores.First();
        var customer = data.Customers.Single();
        data.Carts.Add(new QuickCommerce.Domain.Cart { CustomerId = customer.Id, StoreId = store.Id });

        var empty = await checkoutService.CheckoutAsync(store.Id, new CheckoutRequest("12 Main Street", 19.07, 72.87));
        var invalid = await checkoutService.CheckoutAsync(store.Id, new CheckoutRequest(string.Empty, 100, 200));

        Assert.Equal(CheckoutOperationStatus.Conflict, empty.Status);
        Assert.Equal(CheckoutOperationStatus.InvalidRequest, invalid.Status);
        Assert.Empty(data.Orders);
    }

    private static (CartService Cart, CheckoutService Checkout) CreateServices(InMemoryCommerceStore data)
    {
        var user = data.Users.Single();
        var organization = data.Organizations.Single();
        var currentUser = new HttpContextCurrentUser(new HttpContextAccessor
        {
            HttpContext = new DefaultHttpContext
            {
                User = new ClaimsPrincipal(new ClaimsIdentity(
                [
                    new Claim(ClaimTypes.NameIdentifier, user.ExternalSubject),
                    new Claim("organization_id", organization.Id.ToString())
                ], "Test"))
            }
        });
        var resolver = new CurrentUserContextResolver(currentUser, data);
        var cartService = new CartService(data, resolver, new AddCartItemRequestValidator(), new UpdateCartItemRequestValidator());
        var checkoutService = new CheckoutService(data, resolver, new CheckoutRequestValidator());
        return (cartService, checkoutService);
    }
}