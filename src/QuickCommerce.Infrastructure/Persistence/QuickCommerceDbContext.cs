using Microsoft.EntityFrameworkCore;
using QuickCommerce.Domain;

namespace QuickCommerce.Infrastructure.Persistence;

public sealed class QuickCommerceDbContext(DbContextOptions<QuickCommerceDbContext> options) : DbContext(options)
{
    public DbSet<Category> Categories => Set<Category>();
    public DbSet<Organization> Organizations => Set<Organization>();
    public DbSet<AuthorizationRole> AuthorizationRoles => Set<AuthorizationRole>();
    public DbSet<AuthorizationPermission> AuthorizationPermissions => Set<AuthorizationPermission>();
    public DbSet<RolePermission> RolePermissions => Set<RolePermission>();
    public DbSet<UserRole> UserRoles => Set<UserRole>();
    public DbSet<UserStoreAssignment> UserStoreAssignments => Set<UserStoreAssignment>();
    public DbSet<UserCredential> UserCredentials => Set<UserCredential>();
    public DbSet<RefreshToken> RefreshTokens => Set<RefreshToken>();
    public DbSet<User> Users => Set<User>();
    public DbSet<ApprovalRequest> ApprovalRequests => Set<ApprovalRequest>();
    public DbSet<Customer> Customers => Set<Customer>();
    public DbSet<Notification> Notifications => Set<Notification>();
    public DbSet<PasswordResetCode> PasswordResetCodes => Set<PasswordResetCode>();
    public DbSet<Product> Products => Set<Product>();
    public DbSet<ProductTranslation> ProductTranslations => Set<ProductTranslation>();
    public DbSet<CategoryTranslation> CategoryTranslations => Set<CategoryTranslation>();
    public DbSet<Store> Stores => Set<Store>();
    public DbSet<StoreInventory> StoreInventory => Set<StoreInventory>();
    public DbSet<CustomerAddress> CustomerAddresses => Set<CustomerAddress>();
    public DbSet<ProductVariant> ProductVariants => Set<ProductVariant>();
    public DbSet<StoreVariantInventory> StoreVariantInventory => Set<StoreVariantInventory>();
    public DbSet<Cart> Carts => Set<Cart>();
    public DbSet<CartItem> CartItems => Set<CartItem>();
    public DbSet<CheckoutRequestRecord> CheckoutRequests => Set<CheckoutRequestRecord>();
    public DbSet<Payment> Payments => Set<Payment>();
    public DbSet<OrderNumberCounter> OrderNumberCounters => Set<OrderNumberCounter>();
    public DbSet<Campaign> Campaigns => Set<Campaign>();
    public DbSet<PaymentEvent> PaymentEvents => Set<PaymentEvent>();
    public DbSet<Order> Orders => Set<Order>();
    public DbSet<OrderItem> OrderItems => Set<OrderItem>();
    public DbSet<OrderStatusHistory> OrderStatusHistory => Set<OrderStatusHistory>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(QuickCommerceDbContext).Assembly);
        SeedData.Configure(modelBuilder);
    }
}
