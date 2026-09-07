using QuickCommerce.Application.Interfaces;
using QuickCommerce.Application.DTOs;
using QuickCommerce.Domain;

namespace QuickCommerce.Infrastructure;

public sealed class InMemoryCommerceStore : ICommerceStore
{
    public List<Category> Categories { get; } = [];
    public List<Organization> Organizations { get; } = [];
    public List<User> Users { get; } = [];
    public List<Customer> Customers { get; } = [];
    public List<Notification> Notifications { get; } = [];
    public List<Product> Products { get; } = [];
    public List<Store> Stores { get; } = [];
    public List<StoreInventory> Inventory { get; } = [];
    public List<Cart> Carts { get; } = [];
    public List<Order> Orders { get; } = [];
    public object SyncRoot { get; } = new();

    public Task<IReadOnlyList<Category>> GetCategoriesAsync(CancellationToken cancellationToken = default) => Task.FromResult<IReadOnlyList<Category>>(Categories.ToArray());

    public Task<IReadOnlyList<Product>> GetProductsAsync(string? search, Guid? categoryId, CancellationToken cancellationToken = default) => Task.FromResult<IReadOnlyList<Product>>(Products
        .Where(product => string.IsNullOrWhiteSpace(search) || product.Name.Contains(search, StringComparison.OrdinalIgnoreCase) || product.Description.Contains(search, StringComparison.OrdinalIgnoreCase))
        .Where(product => !categoryId.HasValue || product.CategoryId == categoryId)
        .ToArray());

    public Task<Product?> GetProductAsync(Guid id, CancellationToken cancellationToken = default) => Task.FromResult(Products.FirstOrDefault(product => product.Id == id));

    public Task<IReadOnlyList<Store>> GetStoresAsync(CancellationToken cancellationToken = default) => Task.FromResult<IReadOnlyList<Store>>(Stores.ToArray());

    public Task<IReadOnlyList<StoreInventory>> GetInventoryAsync(CancellationToken cancellationToken = default) => Task.FromResult<IReadOnlyList<StoreInventory>>(Inventory.ToArray());

    public Task<IReadOnlyList<Order>> GetOrdersAsync(CancellationToken cancellationToken = default) => Task.FromResult<IReadOnlyList<Order>>(Orders.ToArray());

    public Task<Order?> GetOrderAsync(Guid id, CancellationToken cancellationToken = default) => Task.FromResult(Orders.FirstOrDefault(order => order.Id == id));

    public Task<IReadOnlyList<Order>> GetCustomerOrdersAsync(Guid userId, Guid organizationId, CancellationToken cancellationToken = default) => Task.FromResult<IReadOnlyList<Order>>(Orders
        .Where(order => order.UserId == userId && Stores.Any(store => store.Id == order.StoreId && store.OrganizationId == organizationId))
        .ToArray());

    public Task<Order?> GetCustomerOrderAsync(Guid orderId, Guid userId, Guid organizationId, CancellationToken cancellationToken = default) => Task.FromResult(Orders
        .FirstOrDefault(order => order.Id == orderId && order.UserId == userId && Stores.Any(store => store.Id == order.StoreId && store.OrganizationId == organizationId)));

    public Task<IReadOnlyList<Notification>> GetNotificationsAsync(Guid customerId, bool unreadOnly, CancellationToken cancellationToken = default) => Task.FromResult<IReadOnlyList<Notification>>(Notifications
        .Where(notification => notification.CustomerId == customerId && (!unreadOnly || !notification.IsRead))
        .ToArray());

    public Task<bool> SetNotificationReadAsync(Guid notificationId, Guid customerId, bool isRead, CancellationToken cancellationToken = default)
    {
        var notification = Notifications.FirstOrDefault(item => item.Id == notificationId && item.CustomerId == customerId);
        if (notification is null)
        {
            return Task.FromResult(false);
        }

        notification.IsRead = isRead;
        return Task.FromResult(true);
    }

    public Task<UserContext?> GetUserContextAsync(string externalSubject, CancellationToken cancellationToken = default) => Task.FromResult<UserContext?>(Users
        .Where(user => user.ExternalSubject == externalSubject && user.IsActive)
        .Select(user => new UserContext(user.Id, user.OrganizationId, user.StoreId, user.Role))
        .FirstOrDefault());

    public Task<Customer?> GetCustomerByUserIdAsync(Guid userId, CancellationToken cancellationToken = default) => Task.FromResult<Customer?>(Customers
        .FirstOrDefault(customer => customer.UserId == userId && customer.IsActive));

    public Task<Store?> GetStoreAsync(Guid id, CancellationToken cancellationToken = default) => Task.FromResult<Store?>(Stores
        .FirstOrDefault(store => store.Id == id));

    public Task<Cart?> GetCartAsync(Guid customerId, Guid storeId, CancellationToken cancellationToken = default) => Task.FromResult<Cart?>(Carts
        .FirstOrDefault(cart => cart.CustomerId == customerId && cart.StoreId == storeId));

    public Task AddCartAsync(Cart cart, CancellationToken cancellationToken = default)
    {
        Carts.Add(cart);
        return Task.CompletedTask;
    }

    public Task SaveCartAsync(Cart cart, CancellationToken cancellationToken = default) => Task.CompletedTask;

    public Task<CheckoutCommitResult> TryCheckoutCartAsync(Guid customerId, Guid storeId, CheckoutRequest request, CancellationToken cancellationToken = default)
    {
        lock (SyncRoot)
        {
            var cart = Carts.FirstOrDefault(item => item.CustomerId == customerId && item.StoreId == storeId);
            if (cart is null)
            {
                return Task.FromResult(new CheckoutCommitResult(CheckoutCommitStatus.CartNotFound));
            }

            if (cart.Items.Count == 0)
            {
                return Task.FromResult(new CheckoutCommitResult(CheckoutCommitStatus.CartEmpty));
            }

            var orderItems = new List<OrderItem>();
            foreach (var cartItem in cart.Items)
            {
                var product = Products.FirstOrDefault(item => item.Id == cartItem.ProductId);
                if (product is null || !product.IsActive)
                {
                    return Task.FromResult(new CheckoutCommitResult(CheckoutCommitStatus.ProductUnavailable));
                }

                if (product.Price != cartItem.UnitPriceSnapshot)
                {
                    return Task.FromResult(new CheckoutCommitResult(CheckoutCommitStatus.PriceChanged));
                }

                var inventory = Inventory.FirstOrDefault(item => item.StoreId == storeId && item.ProductId == cartItem.ProductId);
                if (inventory is null || inventory.AvailableQuantity < cartItem.Quantity)
                {
                    return Task.FromResult(new CheckoutCommitResult(CheckoutCommitStatus.InventoryConflict));
                }

                orderItems.Add(new OrderItem
                {
                    ProductId = product.Id,
                    ProductNameSnapshot = product.Name,
                    UnitPrice = product.Price,
                    Quantity = cartItem.Quantity
                });
            }

            foreach (var cartItem in cart.Items)
            {
                Inventory.Single(item => item.StoreId == storeId && item.ProductId == cartItem.ProductId).AvailableQuantity -= cartItem.Quantity;
            }

            var order = new Order
            {
                OrderNumber = $"ORD-{DateTime.UtcNow:yyyyMMddHHmmss}-{Guid.NewGuid():N}"[..32],
                UserId = Customers.Single(customer => customer.Id == customerId).UserId,
                StoreId = storeId,
                DeliveryAddress = request.DeliveryAddress,
                Latitude = request.Latitude,
                Longitude = request.Longitude,
                Items = orderItems
            };
            order.TotalAmount = orderItems.Sum(item => item.TotalPrice);
            order.StatusHistory.Add(new OrderStatusHistory { Status = OrderStatus.Pending });
            Orders.Add(order);
            cart.Items.Clear();
            cart.UpdatedAt = DateTime.UtcNow;
            return Task.FromResult(new CheckoutCommitResult(CheckoutCommitStatus.Succeeded, MapOrder(order)));
        }
    }

    public Task<OrderLifecycleResult> TryTransitionOrderAsync(Guid orderId, Guid organizationId, Guid? storeId, OrderStatus targetStatus, CancellationToken cancellationToken = default)
    {
        lock (SyncRoot)
        {
            var order = Orders.FirstOrDefault(currentOrder => currentOrder.Id == orderId &&
                (!storeId.HasValue || currentOrder.StoreId == storeId.Value));
            var store = order is null
                ? null
                : Stores.FirstOrDefault(currentStore => currentStore.Id == order.StoreId && currentStore.OrganizationId == organizationId);
            if (order is null || store is null)
            {
                return Task.FromResult(new OrderLifecycleResult(OrderLifecycleStatus.NotFound));
            }

            if (!OrderStatusTransitions.IsValid(order.Status, targetStatus))
            {
                return Task.FromResult(new OrderLifecycleResult(OrderLifecycleStatus.InvalidTransition));
            }

            order.Status = targetStatus;
            order.StatusHistory.Add(new OrderStatusHistory { Status = targetStatus });
            var customer = Customers.FirstOrDefault(customer => customer.UserId == order.UserId);
            if (customer is not null)
            {
                Notifications.Add(CreateStatusNotification(customer.Id, order.Id, targetStatus));
            }
            return Task.FromResult(new OrderLifecycleResult(OrderLifecycleStatus.Succeeded, MapOrder(order)));
        }
    }

    public Task<bool> TryCreateOrderAsync(Order order, IReadOnlyCollection<InventoryAdjustment> inventoryAdjustments, CancellationToken cancellationToken = default)
    {
        lock (SyncRoot)
        {
            foreach (var adjustment in inventoryAdjustments)
            {
                var stock = Inventory.FirstOrDefault(item => item.StoreId == adjustment.StoreId && item.ProductId == adjustment.ProductId);
                if (stock is null || stock.AvailableQuantity < adjustment.Quantity)
                {
                    return Task.FromResult(false);
                }
            }

            foreach (var adjustment in inventoryAdjustments)
            {
                Inventory.First(item => item.StoreId == adjustment.StoreId && item.ProductId == adjustment.ProductId).AvailableQuantity -= adjustment.Quantity;
            }

            Orders.Add(order);
            return Task.FromResult(true);
        }
    }

    private static OrderResponse MapOrder(Order order) => new(
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

    private static Notification CreateStatusNotification(Guid customerId, Guid orderId, OrderStatus status) => new()
    {
        CustomerId = customerId,
        OrderId = orderId,
        Type = "OrderStatusChanged",
        Title = "Order status updated",
        Message = $"Your order is now {status}.",
        IsRead = false
    };

    public static InMemoryCommerceStore CreateSeeded()
    {
        var store = new InMemoryCommerceStore();
        var organization = new Organization { Name = "QuickCart Demo Retailer" };
        store.Organizations.Add(organization);
        var user = new User
        {
            ExternalSubject = "quickcart-demo-user",
            DisplayName = "QuickCart Demo User",
            OrganizationId = organization.Id,
            Organization = organization
        };
        store.Users.Add(user);
        var customer = new Customer { UserId = user.Id, User = user };
        user.Customer = customer;
        store.Customers.Add(customer);
        var groceries = new Category { Name = "Groceries" };
        var vegetables = new Category { Name = "Vegetables" };
        var fruits = new Category { Name = "Fruits" };
        var dairy = new Category { Name = "Dairy" };
        store.Categories.AddRange([groceries, vegetables, fruits, dairy]);

        var products = new[]
        {
            new Product { Sku = "VEG-TOM", Name = "Tomato", Description = "Fresh red tomatoes", Price = 45, UnitOfMeasure = "1 kg", CategoryId = vegetables.Id, ImageUrl = "https://images.unsplash.com/photo-1546094096-0df4bcaaa337?w=640" },
            new Product { Sku = "VEG-POT", Name = "Potato", Description = "Everyday cooking potatoes", Price = 38, UnitOfMeasure = "1 kg", CategoryId = vegetables.Id, ImageUrl = "https://images.unsplash.com/photo-1518977676601-b53f82aba655?w=640" },
            new Product { Sku = "DAI-MILK", Name = "Farm Milk", Description = "Pasteurized full cream milk", Price = 34, UnitOfMeasure = "500 ml", CategoryId = dairy.Id, ImageUrl = "https://images.unsplash.com/photo-1550583724-b2692b85b150?w=640" },
            new Product { Sku = "FRT-APL", Name = "Royal Gala Apples", Description = "Crisp and naturally sweet", Price = 149, UnitOfMeasure = "1 kg", CategoryId = fruits.Id, ImageUrl = "https://images.unsplash.com/photo-1560806887-1e4cd0b6cbd6?w=640" },
            new Product { Sku = "GRO-RICE", Name = "Daily Rice", Description = "Long grain rice for every meal", Price = 89, UnitOfMeasure = "1 kg", CategoryId = groceries.Id, ImageUrl = "https://images.unsplash.com/photo-1586201375761-83865001e31c?w=640" }
        };
        store.Products.AddRange(products);

        store.Stores.AddRange([
            new Store { Name = "Harbor Point Dark Store", Address = "12 Marine Drive", Latitude = 19.076, Longitude = 72.8777, ServiceRadiusKm = 8, OrganizationId = organization.Id, Organization = organization },
            new Store { Name = "Cedar Market Hub", Address = "44 Cedar Avenue", Latitude = 19.102, Longitude = 72.916, ServiceRadiusKm = 7, OrganizationId = organization.Id, Organization = organization },
            new Store { Name = "North Star Fulfillment", Address = "8 Station Road", Latitude = 19.045, Longitude = 72.899, ServiceRadiusKm = 9, OrganizationId = organization.Id, Organization = organization }
        ]);

        var quantities = new[] { 24, 8, 0, 16, 5 };
        foreach (var currentStore in store.Stores)
        {
            for (var index = 0; index < products.Length; index++)
            {
                store.Inventory.Add(new StoreInventory { StoreId = currentStore.Id, ProductId = products[index].Id, AvailableQuantity = Math.Max(0, quantities[index] + store.Stores.IndexOf(currentStore) * 4) });
            }
        }

        return store;
    }
}
