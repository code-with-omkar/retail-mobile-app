namespace QuickCommerce.Domain;

public enum Role { Customer, StoreStaff, Admin, DeliveryPartner, ApplicationAdmin }
public enum StaffCategory { StoreManager, StoreEmployee }
public enum ApprovalStatus { Pending, Approved, Rejected, Cancelled }
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
	Cancelled,

	/// <summary>An online order whose payment has not arrived yet. Its items are held until <see cref="Order.PaymentExpiresAt"/>. Staff do not see it.</summary>
	AwaitingPayment
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
	public string? Code { get; set; }
	public bool IsActive { get; set; } = true;
	public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
	public string? CreatedBy { get; set; }
	public DateTime? UpdatedAt { get; set; }
	public string? UpdatedBy { get; set; }
	public List<Store> Stores { get; set; } = [];
	public List<User> Users { get; set; } = [];
}

public sealed class AuthorizationRole
{
	public Guid Id { get; set; } = Guid.NewGuid();
	public required string Name { get; set; }
	public required string Code { get; set; }
	public string? Description { get; set; }
	public bool IsActive { get; set; } = true;
	public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
	public string? CreatedBy { get; set; }
	public DateTime? UpdatedAt { get; set; }
	public string? UpdatedBy { get; set; }
	public List<UserRole> UserRoles { get; set; } = [];
	public List<RolePermission> RolePermissions { get; set; } = [];
}

public sealed class AuthorizationPermission
{
	public Guid Id { get; set; } = Guid.NewGuid();
	public required string Name { get; set; }
	public required string Code { get; set; }
	public string? Description { get; set; }
	public bool IsActive { get; set; } = true;
	public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
	public string? CreatedBy { get; set; }
	public DateTime? UpdatedAt { get; set; }
	public string? UpdatedBy { get; set; }
	public List<RolePermission> RolePermissions { get; set; } = [];
}

public sealed class RolePermission
{
	public Guid RoleId { get; set; }
	public Guid PermissionId { get; set; }
	public bool IsActive { get; set; } = true;
	public AuthorizationRole Role { get; set; } = null!;
	public AuthorizationPermission Permission { get; set; } = null!;
}

public sealed class UserRole
{
	public Guid UserId { get; set; }
	public Guid RoleId { get; set; }
	public bool IsActive { get; set; } = true;
	public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
	public string? CreatedBy { get; set; }
	public DateTime? UpdatedAt { get; set; }
	public string? UpdatedBy { get; set; }
	public User User { get; set; } = null!;
	public AuthorizationRole Role { get; set; } = null!;
}

public sealed class UserStoreAssignment
{
	public Guid UserId { get; set; }
	public Guid StoreId { get; set; }
	public bool IsActive { get; set; } = true;
	public DateTime EffectiveFrom { get; set; } = DateTime.UtcNow;
	public DateTime? EffectiveTo { get; set; }
	public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
	public string? CreatedBy { get; set; }
	public DateTime? UpdatedAt { get; set; }
	public string? UpdatedBy { get; set; }
	public User User { get; set; } = null!;
	public Store Store { get; set; } = null!;
}

public sealed class User
{
	public Guid Id { get; set; } = Guid.NewGuid();
	public required string ExternalSubject { get; set; }
	public required string DisplayName { get; set; }
	public string? FirstName { get; set; }
	public string? LastName { get; set; }
	public string? Email { get; set; }

	/// <summary>Contact number entered by the customer. Not verified; verification arrives with phone sign-in.</summary>
	public string? PhoneNumber { get; set; }

	/// <summary>When the holder proved they own <see cref="PhoneNumber"/> with a text-message code. Only a verified number can sign in; null = never verified.</summary>
	public DateTime? PhoneVerifiedAt { get; set; }

	/// <summary>When the email was proved (Google confirmed it). Null for accounts made with an email and password, whose email nobody checked.</summary>
	public DateTime? EmailVerifiedAt { get; set; }

	/// <summary>Google's stable id for the person ("sub"), once they have signed in with Google.</summary>
	public string? GoogleSubject { get; set; }
	public Guid OrganizationId { get; set; }
	public Guid? StoreId { get; set; }
	public Role Role { get; set; } = Role.Customer;
	public StaffCategory? StaffCategory { get; set; }
	public bool IsActive { get; set; } = true;
	public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
	public string? CreatedBy { get; set; }
	public DateTime? UpdatedAt { get; set; }
	public string? UpdatedBy { get; set; }
	public Organization Organization { get; set; } = null!;
	public Store? Store { get; set; }
	public Customer? Customer { get; set; }
	public List<UserRole> UserRoles { get; set; } = [];
	public List<UserStoreAssignment> StoreAssignments { get; set; } = [];
	public UserCredential? Credential { get; set; }
	public List<RefreshToken> RefreshTokens { get; set; } = [];
}

public sealed class ApprovalRequest
{
	public Guid Id { get; set; } = Guid.NewGuid();
	public Guid OrganizationId { get; set; }
	public Guid StoreId { get; set; }
	public Guid RequestedByUserId { get; set; }
	public required string EntityType { get; set; }
	public Guid EntityId { get; set; }
	public required string ApprovalType { get; set; }
	public ApprovalStatus Status { get; set; } = ApprovalStatus.Pending;
	public Guid? ApprovedByUserId { get; set; }
	public DateTime? ApprovedAt { get; set; }
	public DateTime? RejectedAt { get; set; }
	public string? RejectionReason { get; set; }
	public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
	public string? CreatedBy { get; set; }
	public DateTime? UpdatedAt { get; set; }
	public string? UpdatedBy { get; set; }
	public Organization Organization { get; set; } = null!;
	public Store Store { get; set; } = null!;
	public User RequestedByUser { get; set; } = null!;
	public User? ApprovedByUser { get; set; }
}

public sealed class UserCredential
{
	public Guid Id { get; set; } = Guid.NewGuid();
	public Guid UserId { get; set; }
	public required string PasswordHash { get; set; }
	public string HashVersion { get; set; } = "ASP.NET Core Identity v3";
	public DateTime PasswordChangedAt { get; set; } = DateTime.UtcNow;
	public int FailedLoginCount { get; set; }
	public DateTime? LockedUntil { get; set; }
	public bool IsActive { get; set; } = true;
	public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
	public string? CreatedBy { get; set; }
	public DateTime? UpdatedAt { get; set; }
	public string? UpdatedBy { get; set; }
	public User User { get; set; } = null!;
}

public sealed class RefreshToken
{
	public Guid Id { get; set; } = Guid.NewGuid();
	public Guid UserId { get; set; }
	public Guid SessionId { get; set; }
	public required string TokenHash { get; set; }
	public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
	public DateTime ExpiresAt { get; set; }
	public DateTime? RevokedAt { get; set; }
	public Guid? ReplacedByTokenId { get; set; }
	public string? CreatedByIp { get; set; }
	public string? RevokedByIp { get; set; }
	public User User { get; set; } = null!;
}

public sealed class Customer
{
	public Guid Id { get; set; } = Guid.NewGuid();
	public Guid UserId { get; set; }
	public bool IsActive { get; set; } = true;

	/// <summary>Whether this customer wants offers and announcements. Order and payment notifications are always sent.</summary>
	public bool OffersEnabled { get; set; } = true;
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

	/// <summary>Optional maximum retail price. Null means no separate MRP (no discount).</summary>
	public decimal? Mrp { get; set; }
	public required string UnitOfMeasure { get; set; }
	public Guid CategoryId { get; set; }
	public string? ImageUrl { get; set; }
	public bool IsActive { get; set; } = true;
}

/// <summary>A password reset code. Only a keyed hash of the code is stored, never the code itself.</summary>
public sealed class PasswordResetCode
{
	public Guid Id { get; set; } = Guid.NewGuid();
	public Guid UserId { get; set; }
	public required string CodeHash { get; set; }
	public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
	public DateTime ExpiresAt { get; set; }
	public int FailedAttempts { get; set; }
	public DateTime? UsedAt { get; set; }
	public string? RequestedFromIp { get; set; }
}

/// <summary>Translated product text. English stays in <see cref="Product.Name"/> and <see cref="Product.Description"/>.</summary>
public sealed class ProductTranslation
{
	public Guid ProductId { get; set; }

	/// <summary>Lowercase language code, for example "mr".</summary>
	public required string Locale { get; set; }
	public required string Name { get; set; }
	public string? Description { get; set; }
}

/// <summary>Translated category name. English stays in <see cref="Category.Name"/>.</summary>
public sealed class CategoryTranslation
{
	public Guid CategoryId { get; set; }
	public required string Locale { get; set; }
	public required string Name { get; set; }
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

	/// <summary>How customers reach the store about an order. Optional; staff fill it in.</summary>
	public string? PhoneNumber { get; set; }

	/// <summary>Short unique code (up to 8 letters or digits) that starts this store's order numbers, for example KHG.</summary>
	public string? Code { get; set; }
	public bool IsActive { get; set; } = true;
	public Organization Organization { get; set; } = null!;
	public List<User> Users { get; set; } = [];
	public List<UserStoreAssignment> UserStoreAssignments { get; set; } = [];
	public List<Cart> Carts { get; set; } = [];
}

/// <summary>
/// A sellable pack of a product (for example 500 g). Every product has exactly one default variant; price, MRP and stock
/// live on the variant. The product's own price columns remain for the admin API until product editing is rebuilt.
/// </summary>
public sealed class ProductVariant
{
	public Guid Id { get; set; } = Guid.NewGuid();
	public Guid ProductId { get; set; }
	public required string Sku { get; set; }

	/// <summary>Shown to customers, for example "500 g".</summary>
	public required string Label { get; set; }
	public decimal Price { get; set; }

	/// <summary>Optional maximum retail price. Null means no separate MRP (no discount).</summary>
	public decimal? Mrp { get; set; }
	public int SortOrder { get; set; }
	public bool IsDefault { get; set; }
	public bool IsActive { get; set; } = true;
}

/// <summary>Stock of one variant in one store. A row means the store carries the variant, whatever the quantity.</summary>
public sealed class StoreVariantInventory
{
	public Guid StoreId { get; set; }
	public Guid VariantId { get; set; }
	public int AvailableQuantity { get; set; }
	public int ReorderThreshold { get; set; } = 5;
	public byte[] RowVersion { get; set; } = [];
}

/// <summary>Stock per product. Superseded by <see cref="StoreVariantInventory"/> (phase P3) and no longer read or written by the application; the table is kept as the rollback copy until a later cleanup.</summary>
public sealed class StoreInventory
{
	public Guid StoreId { get; set; }
	public Guid ProductId { get; set; }
	public int AvailableQuantity { get; set; }
	public int ReorderThreshold { get; set; } = 5;
	public byte[] RowVersion { get; set; } = [];
}

/// <summary>A delivery address saved by a customer. Personal data: only its owner may read or change it.</summary>
public sealed class CustomerAddress
{
	public Guid Id { get; set; } = Guid.NewGuid();
	public Guid CustomerId { get; set; }

	/// <summary>Home, Work or a name the customer chose.</summary>
	public required string Label { get; set; }

	/// <summary>The formatted address from the map or search.</summary>
	public required string Line { get; set; }
	public string FlatOrBuilding { get; set; } = string.Empty;
	public string Landmark { get; set; } = string.Empty;
	public double Latitude { get; set; }
	public double Longitude { get; set; }

	/// <summary>Who receives the delivery, and how to reach them.</summary>
	public required string ReceiverName { get; set; }
	public required string ReceiverPhone { get; set; }

	/// <summary>Exactly one address per customer is the default (enforced by a filtered unique index).</summary>
	public bool IsDefault { get; set; }
	public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
	public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
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
	public Guid VariantId { get; set; }
	public string ProductNameSnapshot { get; set; } = string.Empty;
	public string VariantLabelSnapshot { get; set; } = string.Empty;
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
	/// <summary>What the customer pays: <see cref="SubtotalAmount"/> + <see cref="DeliveryFee"/> + <see cref="HandlingFee"/>.</summary>
	public decimal TotalAmount { get; set; }
	public decimal SubtotalAmount { get; set; }
	public decimal DeliveryFee { get; set; }
	public decimal HandlingFee { get; set; }

	/// <summary>How the customer pays. Only <see cref="PaymentMethods.CashOnDelivery"/> exists until online payment (P7).</summary>
	public string PaymentMethod { get; set; } = PaymentMethods.CashOnDelivery;
	public OrderStatus Status { get; set; } = OrderStatus.Pending;
	public required string DeliveryAddress { get; set; }
	public double Latitude { get; set; }
	public double Longitude { get; set; }

	/// <summary>Copied from the saved address when the order was placed with one; later edits to the address do not change the order.</summary>
	public string? ReceiverName { get; set; }
	public string? ReceiverPhone { get; set; }
	public Guid? DeliveryAddressId { get; set; }

	/// <summary>The arrival estimate given when the order was placed. Null for orders placed before it was kept.</summary>
	public int? EstimatedDeliveryMinutes { get; set; }

	/// <summary>Where the money stands. Cash on delivery orders are NotRequired. Mirrors the status of the order's <see cref="Payment"/>.</summary>
	public PaymentState PaymentStatus { get; set; } = PaymentState.NotRequired;

	/// <summary>For an order awaiting payment: until when its items are held.</summary>
	public DateTime? PaymentExpiresAt { get; set; }

	/// <summary>The delivery partner the store gave a Ready order to. Null until assigned (and for orders the customer collects).</summary>
	public Guid? DeliveryPartnerId { get; set; }
	public DateTime? AssignedAt { get; set; }

	/// <summary>Four digits the customer tells the partner at the door. Only the customer and the partner who holds the order ever see it.</summary>
	public string? DeliveryOtp { get; set; }
	public int OtpFailures { get; set; }
	public DateTime? OtpLockedUntil { get; set; }
	public DateTime? PickedUpAt { get; set; }
	public DateTime? DeliveredAt { get; set; }

	/// <summary>What the partner reports collecting. Cash on delivery orders must equal the total; online orders are 0.</summary>
	public decimal? CashCollected { get; set; }
	public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
	public byte[] RowVersion { get; set; } = [];
	public List<OrderItem> Items { get; set; } = [];
	public List<OrderStatusHistory> StatusHistory { get; set; } = [];
}

public static class PaymentMethods
{
	public const string CashOnDelivery = "CashOnDelivery";

	/// <summary>Paid in the app through the payment provider (UPI, card, netbanking).</summary>
	public const string Online = "Online";
}

public enum PaymentState
{
	/// <summary>Cash on delivery: nothing is paid in the app.</summary>
	NotRequired,

	/// <summary>Waiting for the customer to pay (or to try again after a failed attempt).</summary>
	Created,

	/// <summary>The last attempt failed. The customer may try again until the hold runs out.</summary>
	Failed,
	Paid,

	/// <summary>The order was cancelled or declined after payment (or the payment came too late): the refund is to be made or is on its way.</summary>
	Refunding,
	Refunded,

	/// <summary>The provider could not make the refund. Retried, and listed for staff.</summary>
	RefundFailed
}

/// <summary>One online payment for one order, as the provider reports it. Amounts are in paise, as the provider counts them.</summary>
public sealed class Payment
{
	public Guid Id { get; set; } = Guid.NewGuid();
	public Guid OrderId { get; set; }
	public string Provider { get; set; } = "Razorpay";

	/// <summary>The provider's own order id for this payment (created when the customer starts paying).</summary>
	public string? ProviderOrderId { get; set; }
	public string? ProviderPaymentId { get; set; }

	/// <summary>What the customer owes, worked out by the server when the order was placed. Never taken from the app.</summary>
	public long AmountPaise { get; set; }
	public string Currency { get; set; } = "INR";
	public PaymentState Status { get; set; } = PaymentState.Created;

	/// <summary>How many times the customer has started to pay.</summary>
	public int Attempts { get; set; }
	public string? FailureReason { get; set; }
	public string? RefundId { get; set; }
	public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
	public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
	public byte[] RowVersion { get; set; } = [];
}

/// <summary>A notification from the provider that has been handled, so the same one arriving again does nothing.</summary>
public sealed class PaymentEvent
{
	public Guid Id { get; set; } = Guid.NewGuid();
	public string Provider { get; set; } = "Razorpay";
	public required string EventId { get; set; }
	public required string Type { get; set; }
	public DateTime ReceivedAt { get; set; } = DateTime.UtcNow;
}

/// <summary>
/// Remembers that a checkout with this key already produced an order, so a retry or double tap gets that order back instead of a second one.
/// Written in the same transaction as the order.
/// </summary>
public sealed class CheckoutRequestRecord
{
	public Guid Id { get; set; } = Guid.NewGuid();
	public Guid CustomerId { get; set; }
	public required string IdempotencyKey { get; set; }

	/// <summary>Hash of the store and delivery address the key was first used with. The same key with something else is a mistake, not a retry.</summary>
	public required string RequestHash { get; set; }
	public Guid OrderId { get; set; }
	public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
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

	/// <summary>Order, Payment, Offer or System (see <see cref="NotificationCategories"/>).</summary>
	public string Category { get; set; } = NotificationCategories.Order;

	/// <summary>Small data the words are filled from, for example {"orderNumber":"KHG-261009-0042"}.</summary>
	public string? DataJson { get; set; }

	/// <summary>Set for offers: the campaign that sent it (its words are used when the notification is shown).</summary>
	public Guid? CampaignId { get; set; }
	public Campaign? Campaign { get; set; }
	public Customer Customer { get; set; } = null!;
}

public static class NotificationCategories
{
	public const string Order = "Order";
	public const string Payment = "Payment";
	public const string Offer = "Offer";
	public const string System = "System";
}

/// <summary>The kinds of notification. The words for each are in the notification catalogue.</summary>
public static class NotificationTypes
{
	public const string OrderPlaced = "OrderPlaced";
	public const string OrderAccepted = "OrderAccepted";
	public const string OrderPacking = "OrderPacking";
	public const string OutForDelivery = "OutForDelivery";
	public const string OrderDelivered = "OrderDelivered";
	public const string OrderRejected = "OrderRejected";
	public const string OrderCancelled = "OrderCancelled";
	public const string PaymentReceived = "PaymentReceived";
	public const string PaymentNotCompleted = "PaymentNotCompleted";
	public const string PaymentProblem = "PaymentProblem";
	public const string PaymentWillBeRefunded = "PaymentWillBeRefunded";
	public const string RefundProcessed = "RefundProcessed";
	public const string Offer = "Offer";
}

/// <summary>An offer or announcement written by an admin. When its start time comes it is sent once to every customer who has offers switched on.</summary>
public sealed class Campaign
{
	public Guid Id { get; set; } = Guid.NewGuid();
	public required string TitleEn { get; set; }
	public required string BodyEn { get; set; }
	public string? TitleMr { get; set; }
	public string? BodyMr { get; set; }
	public DateTime StartsAt { get; set; } = DateTime.UtcNow;

	/// <summary>False once cancelled before it was sent.</summary>
	public bool IsActive { get; set; } = true;

	/// <summary>When it was sent; null while it is waiting for its start time.</summary>
	public DateTime? PublishedAt { get; set; }
	public int RecipientCount { get; set; }
	public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
	public string? CreatedBy { get; set; }

	/// <summary>The stores this offer is from. Empty means every store (an offer from the whole business).</summary>
	public List<CampaignStore> Stores { get; set; } = [];

	/// <summary>What the store pays to publish this offer, in paise, as it was when the offer was created. 0 means free (and offers an administrator creates are free).</summary>
	public int FeePaise { get; set; }

	/// <summary>NotRequired while free; otherwise the offer is sent only once this is Paid. Refunding or Refunded after a paid offer is cancelled before it was sent.</summary>
	public PaymentState PaymentStatus { get; set; } = PaymentState.NotRequired;
}

/// <summary>
/// The fee a store pays to publish an offer. One row with no store is the default for every store; a row with a store overrides it for that
/// store (0 makes that store's offers free). The most specific row wins.
/// </summary>
public sealed class OfferFee
{
	public Guid Id { get; set; } = Guid.NewGuid();
	public Guid? StoreId { get; set; }
	public int AmountPaise { get; set; }
	public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
	public string? UpdatedBy { get; set; }
}

/// <summary>The online payment of an offer's fee (one per offer), made through the payment provider like an order's payment.</summary>
public sealed class CampaignPayment
{
	public Guid Id { get; set; } = Guid.NewGuid();
	public Guid CampaignId { get; set; }
	public string Provider { get; set; } = "Razorpay";
	public string? ProviderOrderId { get; set; }
	public string? ProviderPaymentId { get; set; }
	public long AmountPaise { get; set; }
	public string Currency { get; set; } = "INR";
	public PaymentState Status { get; set; } = PaymentState.Created;
	public int Attempts { get; set; }
	public string? FailureReason { get; set; }
	public string? RefundId { get; set; }
	public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
	public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
	public byte[] RowVersion { get; set; } = [];
}

/// <summary>One store an offer is from.</summary>
public sealed class CampaignStore
{
	public Guid CampaignId { get; set; }
	public Guid StoreId { get; set; }
	public Store Store { get; set; } = null!;
}

public sealed class OrderItem
{
	public Guid ProductId { get; set; }

	/// <summary>Null on orders placed before variants existed.</summary>
	public Guid? VariantId { get; set; }
	public required string ProductNameSnapshot { get; set; }

	/// <summary>The pack label at the time of the order, for example "500 g". Null on orders placed before variants existed.</summary>
	public string? VariantLabelSnapshot { get; set; }
	public decimal UnitPrice { get; set; }

	/// <summary>The variant's MRP at the time of the order, when it had one.</summary>
	public decimal? UnitMrpSnapshot { get; set; }
	public int Quantity { get; set; }
	public decimal TotalPrice => UnitPrice * Quantity;
}

public sealed class OrderStatusHistory
{
	public OrderStatus Status { get; set; }
	public DateTime ChangedAt { get; set; } = DateTime.UtcNow;
}

/// <summary>The last order number given out by a store on a day. One row per store and day; the next number is this plus one.</summary>
public sealed class OrderNumberCounter
{
	public Guid StoreId { get; set; }
	public DateOnly Day { get; set; }
	public int LastNumber { get; set; }
}

/// <summary>Order numbers look like KHG-261009-0042: the store's code, the day (yyMMdd, India time) and that store's count for the day.</summary>
public static class OrderNumbers
{
	private static readonly TimeSpan India = TimeSpan.FromHours(5.5);

	/// <summary>The business day of a moment: the calendar day in India, so a number's date matches what the customer sees on their clock.</summary>
	public static DateOnly DayOf(DateTime utcNow) => DateOnly.FromDateTime(DateTime.SpecifyKind(utcNow, DateTimeKind.Utc).Add(India));

	/// <summary>The store's code, or (for a store without one) a short stand-in taken from its id, so a number can always be made.</summary>
	public static string CodeOf(Store store) => string.IsNullOrWhiteSpace(store.Code) ? store.Id.ToString("N")[..4].ToUpperInvariant() : store.Code.Trim().ToUpperInvariant();

	public static string Format(string storeCode, DateOnly day, int number) => $"{storeCode}-{day:yyMMdd}-{number:0000}";
}

/// <summary>Where a placement bid stands. Only one bid per pincode and week can be Leading.</summary>
public enum PlacementBidStatus
{
	/// <summary>Placed but not paid yet.</summary>
	AwaitingPayment,

	/// <summary>Paid and the highest paid bid for its pincode and week: that store is shown first there for that week.</summary>
	Leading,

	/// <summary>Paid but beaten by a higher paid bid (or arrived after one): refunded in full.</summary>
	Outbid,

	/// <summary>Withdrawn before it was paid, or cancelled by an administrator (a paid one is refunded).</summary>
	Cancelled
}

/// <summary>
/// A store's bid to be shown first in a pincode for one week, paid online up front. The highest paid bid for a pincode and week leads;
/// a higher paid bid replaces it and the beaten bid is refunded in full.
/// </summary>
public sealed class PlacementBid
{
	public Guid Id { get; set; } = Guid.NewGuid();
	public Guid StoreId { get; set; }

	/// <summary>The six-digit Indian pincode the bid is for.</summary>
	public required string Pincode { get; set; }

	/// <summary>The Monday (India) the week starts; the slot runs until the next Monday and bidding closes when it starts.</summary>
	public DateOnly WeekStart { get; set; }
	public long AmountPaise { get; set; }
	public PlacementBidStatus Status { get; set; } = PlacementBidStatus.AwaitingPayment;
	public PaymentState PaymentStatus { get; set; } = PaymentState.Created;
	public string Provider { get; set; } = "Razorpay";
	public string? ProviderOrderId { get; set; }
	public string? ProviderPaymentId { get; set; }
	public string Currency { get; set; } = "INR";
	public int Attempts { get; set; }
	public string? FailureReason { get; set; }
	public string? RefundId { get; set; }
	public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
	public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
	public byte[] RowVersion { get; set; } = [];
	public Store Store { get; set; } = null!;
}

/// <summary>The rules of paid placement, set by an administrator. One row; without it the defaults apply.</summary>
public sealed class PlacementRules
{
	public Guid Id { get; set; } = Guid.Parse("00000000-0000-0000-0000-0000000000a1");

	/// <summary>Whether paid placement is on at all. Off: no new bids, and the customer's list is in its normal order.</summary>
	public bool Enabled { get; set; } = true;
	public long MinBidPaise { get; set; } = 50_000;

	/// <summary>How much more than the current highest paid bid a new bid must be.</summary>
	public long MinStepPaise { get; set; } = 10_000;

	/// <summary>How many weeks ahead a store can bid (the next week is 1).</summary>
	public int MaxWeeksAhead { get; set; } = 4;
	public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
	public string? UpdatedBy { get; set; }
}

/// <summary>Weeks for placement: they start on Monday 00:00 India time.</summary>
public static class PlacementWeeks
{
	private static readonly TimeSpan India = TimeSpan.FromHours(5.5);

	/// <summary>The Monday of the India calendar week this moment is in.</summary>
	public static DateOnly WeekStartOf(DateTime utcNow)
	{
		var day = OrderNumbers.DayOf(utcNow);
		var back = ((int)day.DayOfWeek + 6) % 7;
		return day.AddDays(-back);
	}

	/// <summary>The moment (UTC) a week starts: its Monday at 00:00 India time. Bidding for the week closes then.</summary>
	public static DateTime StartUtc(DateOnly weekStart) => DateTime.SpecifyKind(weekStart.ToDateTime(TimeOnly.MinValue) - India, DateTimeKind.Utc);

	public static bool IsMonday(DateOnly day) => day.DayOfWeek == DayOfWeek.Monday;

	/// <summary>A six-digit Indian pincode (the first digit is 1 to 9).</summary>
	public static bool IsPincode(string? value) => value is { Length: 6 } && value[0] is >= '1' and <= '9' && value.All(char.IsAsciiDigit);
}

/// <summary>
/// A text-message code sent to a phone number, to sign in or to verify the number for an account. Only a keyed hash of the code is stored.
/// </summary>
public sealed class OtpChallenge
{
	public Guid Id { get; set; } = Guid.NewGuid();

	/// <summary>The number in international form, for example +919876543210.</summary>
	public required string Phone { get; set; }

	/// <summary>"SignIn" or "VerifyPhone".</summary>
	public required string Purpose { get; set; }

	/// <summary>For VerifyPhone: the signed-in account the number is being verified for.</summary>
	public Guid? UserId { get; set; }
	public required string CodeHash { get; set; }
	public DateTime CreatedAt { get; set; }
	public DateTime ExpiresAt { get; set; }
	public int FailedAttempts { get; set; }
	public DateTime? UsedAt { get; set; }
	public string? RequestedFromIp { get; set; }
}

/// <summary>A product a customer saved to come back to. Part of the customer's personal data: erased when the account is deleted.</summary>
public sealed class FavouriteProduct
{
	public Guid CustomerId { get; set; }
	public Guid ProductId { get; set; }
	public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}

/// <summary>
/// What a customer thought of a delivered order: stars for the shop and, when a delivery partner brought it, for the delivery, with an optional comment.
/// One per order. When the customer deletes their account the comment and the link to them are erased; the stars stay in the shop's figures.
/// </summary>
public sealed class OrderRating
{
	public Guid OrderId { get; set; }
	public Guid StoreId { get; set; }
	public Guid? CustomerId { get; set; }
	public Guid? DeliveryPartnerId { get; set; }
	public int StoreStars { get; set; }
	public int? DeliveryStars { get; set; }
	public string? Comment { get; set; }
	public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
	public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}
