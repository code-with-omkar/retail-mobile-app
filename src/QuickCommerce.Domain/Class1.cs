namespace QuickCommerce.Domain;

public enum Role { Customer, StoreStaff, Admin }
public enum OrderStatus
{
	Pending,
	Accepted,
	Preparing,
	Ready,
	Completed,
	Rejected,
	Confirmed,
	OutForDelivery,
	Delivered,
	Cancelled
}

public static class OrderStatusTransitions
{
	public static bool IsValid(OrderStatus from, OrderStatus to) =>
		(from, to) is
			(OrderStatus.Pending, OrderStatus.Accepted) or
			(OrderStatus.Accepted, OrderStatus.Preparing) or
			(OrderStatus.Preparing, OrderStatus.Ready) or
			(OrderStatus.Ready, OrderStatus.Completed) or
			(OrderStatus.Pending, OrderStatus.Rejected);
}

public sealed class Organization
{
	public Guid Id { get; set; } = Guid.NewGuid();
	public required string Name { get; set; }
	public bool IsActive { get; set; } = true;
	public List<Store> Stores { get; set; } = [];
	public List<User> Users { get; set; } = [];
}

public sealed class User
{
	public Guid Id { get; set; } = Guid.NewGuid();
	public required string ExternalSubject { get; set; }
	public required string DisplayName { get; set; }
	public Guid OrganizationId { get; set; }
	public Guid? StoreId { get; set; }
	public Role Role { get; set; } = Role.Customer;
	public bool IsActive { get; set; } = true;
	public Organization Organization { get; set; } = null!;
	public Store? Store { get; set; }
	public Customer? Customer { get; set; }
}

public sealed class Customer
{
	public Guid Id { get; set; } = Guid.NewGuid();
	public Guid UserId { get; set; }
	public bool IsActive { get; set; } = true;
	public User User { get; set; } = null!;
	public List<Cart> Carts { get; set; } = [];
	public List<Notification> Notifications { get; set; } = [];
}

public sealed class Category
{
	public Guid Id { get; set; } = Guid.NewGuid();
	public required string Name { get; set; }
	public Guid? ParentCategoryId { get; set; }
	public bool IsActive { get; set; } = true;
}

public sealed class Product
{
	public Guid Id { get; set; } = Guid.NewGuid();
	public required string Sku { get; set; }
	public required string Name { get; set; }
	public required string Description { get; set; }
	public decimal Price { get; set; }
	public required string UnitOfMeasure { get; set; }
	public Guid CategoryId { get; set; }
	public string? ImageUrl { get; set; }
	public bool IsActive { get; set; } = true;
}

public sealed class Store
{
	public Guid Id { get; set; } = Guid.NewGuid();
	public Guid OrganizationId { get; set; }
	public required string Name { get; set; }
	public required string Address { get; set; }
	public double Latitude { get; set; }
	public double Longitude { get; set; }
	public double ServiceRadiusKm { get; set; } = 8;
	public bool IsActive { get; set; } = true;
	public Organization Organization { get; set; } = null!;
	public List<User> Users { get; set; } = [];
	public List<Cart> Carts { get; set; } = [];
}

public sealed class StoreInventory
{
	public Guid StoreId { get; set; }
	public Guid ProductId { get; set; }
	public int AvailableQuantity { get; set; }
	public int ReorderThreshold { get; set; } = 5;
	public byte[] RowVersion { get; set; } = [];
}

public sealed class Cart
{
	public Guid Id { get; set; } = Guid.NewGuid();
	public Guid CustomerId { get; set; }
	public Guid StoreId { get; set; }
	public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
	public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
	public Customer Customer { get; set; } = null!;
	public Store Store { get; set; } = null!;
	public List<CartItem> Items { get; set; } = [];
}

public sealed class CartItem
{
	public Guid CartId { get; set; }
	public Guid ProductId { get; set; }
	public string ProductNameSnapshot { get; set; } = string.Empty;
	public decimal UnitPriceSnapshot { get; set; }
	public int Quantity { get; set; }
	public DateTime AddedAt { get; set; } = DateTime.UtcNow;
	public Cart Cart { get; set; } = null!;
	public Product Product { get; set; } = null!;
	public decimal TotalPrice => UnitPriceSnapshot * Quantity;
}

public sealed class Order
{
	public Guid Id { get; set; } = Guid.NewGuid();
	public required string OrderNumber { get; set; }
	public Guid UserId { get; set; }
	public Guid StoreId { get; set; }
	public decimal TotalAmount { get; set; }
	public OrderStatus Status { get; set; } = OrderStatus.Pending;
	public required string DeliveryAddress { get; set; }
	public double Latitude { get; set; }
	public double Longitude { get; set; }
	public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
	public byte[] RowVersion { get; set; } = [];
	public List<OrderItem> Items { get; set; } = [];
	public List<OrderStatusHistory> StatusHistory { get; set; } = [];
}

public sealed class Notification
{
	public Guid Id { get; set; } = Guid.NewGuid();
	public Guid CustomerId { get; set; }
	public Guid? OrderId { get; set; }
	public required string Type { get; set; }
	public required string Title { get; set; }
	public required string Message { get; set; }
	public bool IsRead { get; set; }
	public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
	public Customer Customer { get; set; } = null!;
}

public sealed class OrderItem
{
	public Guid ProductId { get; set; }
	public required string ProductNameSnapshot { get; set; }
	public decimal UnitPrice { get; set; }
	public int Quantity { get; set; }
	public decimal TotalPrice => UnitPrice * Quantity;
}

public sealed class OrderStatusHistory
{
	public OrderStatus Status { get; set; }
	public DateTime ChangedAt { get; set; } = DateTime.UtcNow;
}
