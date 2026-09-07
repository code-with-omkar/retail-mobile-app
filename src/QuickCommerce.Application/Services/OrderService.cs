using FluentValidation;
using QuickCommerce.Application.DTOs;
using QuickCommerce.Application.Interfaces;
using QuickCommerce.Domain;

namespace QuickCommerce.Application.Services;

public sealed class OrderService(ICommerceStore data, IStoreSelectionService storeSelectionService, IValidator<CreateOrderRequest> validator) : IOrderService
{
    public async Task<IReadOnlyList<OrderResponse>> GetOrdersAsync(CancellationToken cancellationToken = default) => (await data.GetOrdersAsync(cancellationToken))
        .OrderByDescending(order => order.CreatedAt)
        .Select(Map)
        .ToArray();

    public async Task<OrderResponse?> GetOrderAsync(Guid id, CancellationToken cancellationToken = default) => await data.GetOrderAsync(id, cancellationToken) is { } order
        ? Map(order)
        : null;

    public async Task<CreateOrderResult> CreateOrderAsync(CreateOrderRequest request, CancellationToken cancellationToken = default)
    {
        var validation = await validator.ValidateAsync(request, cancellationToken);
        if (!validation.IsValid)
        {
            return CreateOrderResult.Invalid(string.Join("; ", validation.Errors.Select(error => error.ErrorMessage)));
        }

        var productIds = request.Items.Select(item => item.ProductId).Distinct().ToArray();
        var stores = await data.GetStoresAsync(cancellationToken);
        var inventory = await data.GetInventoryAsync(cancellationToken);
        var store = storeSelectionService.FindNearest(request.Latitude, request.Longitude, productIds, stores, inventory);
        if (store is null)
        {
            return CreateOrderResult.NoStore("No serviceable store can fulfill this order");
        }

        var products = await data.GetProductsAsync(null, null, cancellationToken);
        var lines = new List<OrderItem>();
        foreach (var requestItem in request.Items)
        {
            var product = products.FirstOrDefault(item => item.Id == requestItem.ProductId && item.IsActive);
            var stock = inventory.FirstOrDefault(item => item.StoreId == store.Id && item.ProductId == requestItem.ProductId);
            if (product is null || stock is null || stock.AvailableQuantity < requestItem.Quantity)
            {
                return CreateOrderResult.InventoryConflict($"Insufficient inventory for product {requestItem.ProductId}");
            }

            lines.Add(new OrderItem { ProductId = product.Id, ProductNameSnapshot = product.Name, UnitPrice = product.Price, Quantity = requestItem.Quantity });
        }

        var order = new Order
        {
            OrderNumber = $"ORD-{DateTime.UtcNow:yyyyMMddHHmmss}",
            UserId = request.UserId,
            StoreId = store.Id,
            DeliveryAddress = request.DeliveryAddress,
            Latitude = request.Latitude,
            Longitude = request.Longitude,
            Items = lines
        };
        order.TotalAmount = lines.Sum(line => line.TotalPrice);
        order.StatusHistory.Add(new OrderStatusHistory { Status = OrderStatus.Pending });

        var adjustments = request.Items.Select(item => new InventoryAdjustment(store.Id, item.ProductId, item.Quantity)).ToArray();
        if (!await data.TryCreateOrderAsync(order, adjustments, cancellationToken))
        {
            return CreateOrderResult.InventoryConflict("Inventory changed while the order was being created");
        }

        return CreateOrderResult.Created(Map(order));
    }

    private static OrderResponse Map(Order order) => new(
        order.Id,
        order.OrderNumber,
        order.UserId,
        order.StoreId,
        order.TotalAmount,
        order.Status,
        order.DeliveryAddress,
        order.Latitude,
        order.Longitude,
        order.CreatedAt,
        order.Items.Select(item => new OrderItemResponse(item.ProductId, item.ProductNameSnapshot, item.UnitPrice, item.Quantity, item.TotalPrice)).ToArray(),
        order.StatusHistory.Select(history => new OrderStatusHistoryResponse(history.Status, history.ChangedAt)).ToArray());
}
