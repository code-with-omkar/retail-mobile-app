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

public sealed class CartServiceTests
{
    [Fact]
    public async Task Add_item_creates_store_cart_and_preserves_price_snapshot()
    {
        var data = InMemoryCommerceStore.CreateSeeded();
        var service = CreateService(data);
        var store = data.Stores.First();
        var product = data.Products.First();

        var created = await service.AddItemAsync(store.Id, new AddCartItemRequest(product.Id, 2));

        Assert.Equal(CartOperationStatus.Succeeded, created.Status);
        Assert.NotNull(created.Cart);
        Assert.Equal(store.Id, created.Cart!.StoreId);
        Assert.Equal(product.Price * 2, created.Cart.TotalAmount);

        product.Price += 10;
        product.Name = "Renamed product";
        var addedAgain = await service.AddItemAsync(store.Id, new AddCartItemRequest(product.Id, 1));

        var item = Assert.Single(addedAgain.Cart!.Items);
        Assert.Equal(3, item.Quantity);
        Assert.Equal(45m, item.UnitPriceSnapshot);
        Assert.Equal("Tomato", item.ProductNameSnapshot);
    }

    [Fact]
    public async Task Update_and_remove_item_change_only_the_customer_cart()
    {
        var data = InMemoryCommerceStore.CreateSeeded();
        var service = CreateService(data);
        var store = data.Stores.First();
        var product = data.Products.First();
        var originalQuantity = data.Inventory.First(item => item.StoreId == store.Id && item.ProductId == product.Id).AvailableQuantity;

        await service.AddItemAsync(store.Id, new AddCartItemRequest(product.Id, 2));
        var updated = await service.UpdateItemAsync(store.Id, product.Id, new UpdateCartItemRequest(5));

        Assert.Equal(CartOperationStatus.Succeeded, updated.Status);
        Assert.Equal(5, Assert.Single(updated.Cart!.Items).Quantity);

        var removed = await service.RemoveItemAsync(store.Id, product.Id);

        Assert.Equal(CartOperationStatus.Succeeded, removed.Status);
        Assert.Empty(removed.Cart!.Items);
        Assert.Equal(originalQuantity, data.Inventory.First(item => item.StoreId == store.Id && item.ProductId == product.Id).AvailableQuantity);
    }

    [Fact]
    public async Task Cart_operations_reject_a_store_outside_the_user_organization()
    {
        var data = InMemoryCommerceStore.CreateSeeded();
        var otherOrganization = new QuickCommerce.Domain.Organization { Name = "Other Retailer" };
        var otherStore = new QuickCommerce.Domain.Store
        {
            OrganizationId = otherOrganization.Id,
            Organization = otherOrganization,
            Name = "Other Store",
            Address = "Other address",
            Latitude = 19,
            Longitude = 72
        };
        data.Organizations.Add(otherOrganization);
        data.Stores.Add(otherStore);
        var service = CreateService(data);

        var result = await service.AddItemAsync(otherStore.Id, new AddCartItemRequest(data.Products.First().Id, 1));

        Assert.Equal(CartOperationStatus.Unauthorized, result.Status);
        Assert.Empty(data.Carts);
    }

    [Fact]
    public async Task Add_item_validates_product_and_quantity_before_creating_cart()
    {
        var data = InMemoryCommerceStore.CreateSeeded();
        var service = CreateService(data);
        var store = data.Stores.First();

        var result = await service.AddItemAsync(store.Id, new AddCartItemRequest(Guid.Empty, 0));

        Assert.Equal(CartOperationStatus.InvalidRequest, result.Status);
        Assert.Empty(data.Carts);
    }

    private static CartService CreateService(InMemoryCommerceStore data)
    {
        var user = data.Users.Single();
        var organization = data.Users.Single(user => user.Id == data.Customers.Single().UserId).Organization;
        var httpContext = new DefaultHttpContext
        {
            User = new ClaimsPrincipal(new ClaimsIdentity(
            [
                new Claim(ClaimTypes.NameIdentifier, user.ExternalSubject),
                new Claim("organization_id", organization.Id.ToString())
            ], "Test"))
        };
        var currentUser = new HttpContextCurrentUser(new HttpContextAccessor { HttpContext = httpContext });
        var resolver = new CurrentUserContextResolver(currentUser, data);
        return new CartService(data, resolver, new AddCartItemRequestValidator(), new UpdateCartItemRequestValidator());
    }
}