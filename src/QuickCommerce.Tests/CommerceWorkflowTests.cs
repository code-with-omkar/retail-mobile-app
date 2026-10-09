using QuickCommerce.Application;
using QuickCommerce.Application.DTOs;
using QuickCommerce.Application.Services;
using QuickCommerce.Application.Validators;
using QuickCommerce.Infrastructure;
using Xunit;

namespace QuickCommerce.Tests;

public sealed class CommerceWorkflowTests
{
    [Fact]
    public void StoreSelection_returns_the_nearest_serviceable_store()
    {
        var data = InMemoryCommerceStore.CreateSeeded();
        var variantId = data.DefaultVariantOf(data.Products[0]).Id;
        var service = new StoreSelectionService();

        var result = service.FindNearest(19.076, 72.8777, [variantId], data.Stores, data.Inventory);

        Assert.NotNull(result);
        Assert.Equal("Harbor Point Dark Store", result.Name);
    }

    [Fact]
    public async Task OrderCreation_decrements_inventory_and_snapshots_price_and_name()
    {
        var data = InMemoryCommerceStore.CreateSeeded();
        var product = data.Products[0];
        var initialQuantity = data.Inventory.First(stock => stock.VariantId == data.DefaultVariantOf(product).Id).AvailableQuantity;
        var service = new OrderService(data, new StoreSelectionService(), new CreateOrderRequestValidator());

        var result = await service.CreateOrderAsync(new CreateOrderRequest(
            Guid.NewGuid(),
            19.076,
            72.8777,
            "12 Marine Drive",
            [new OrderLineRequest(product.Id, 2)]));

        Assert.Equal(CreateOrderStatus.Created, result.Status);
        Assert.NotNull(result.Order);
        Assert.Equal(product.Name, result.Order.Items[0].ProductNameSnapshot);
        Assert.Equal(product.Price, result.Order.Items[0].UnitPrice);
        Assert.Equal(initialQuantity - 2, data.Inventory.First(stock => stock.StoreId == result.Order.StoreId && stock.VariantId == data.DefaultVariantOf(product).Id).AvailableQuantity);
    }

    [Fact]
    public async Task OrderCreation_returns_inventory_conflict_when_quantity_is_unavailable()
    {
        var data = InMemoryCommerceStore.CreateSeeded();
        var product = data.Products[0];
        var service = new OrderService(data, new StoreSelectionService(), new CreateOrderRequestValidator());

        var result = await service.CreateOrderAsync(new CreateOrderRequest(
            Guid.NewGuid(),
            19.076,
            72.8777,
            "12 Marine Drive",
            [new OrderLineRequest(product.Id, 1000)]));

        Assert.Equal(CreateOrderStatus.InventoryConflict, result.Status);
        Assert.Null(result.Order);
    }

    [Fact]
    public async Task OrderCreation_preserves_the_price_snapshot_after_product_price_changes()
    {
        var data = InMemoryCommerceStore.CreateSeeded();
        var product = data.Products[0];
        var originalPrice = product.Price;
        var service = new OrderService(data, new StoreSelectionService(), new CreateOrderRequestValidator());

        var result = await service.CreateOrderAsync(new CreateOrderRequest(
            Guid.NewGuid(),
            19.076,
            72.8777,
            "12 Marine Drive",
            [new OrderLineRequest(product.Id, 1)]));

        product.Price = originalPrice + 100;

        Assert.NotNull(result.Order);
        Assert.Equal(originalPrice, result.Order.Items[0].UnitPrice);
    }
}
