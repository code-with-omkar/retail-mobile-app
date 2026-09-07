using Microsoft.EntityFrameworkCore;
using QuickCommerce.Domain;

namespace QuickCommerce.Infrastructure.Persistence;

public sealed class QuickCommerceDbContext(DbContextOptions<QuickCommerceDbContext> options) : DbContext(options)
{
    public DbSet<Category> Categories => Set<Category>();
    public DbSet<Organization> Organizations => Set<Organization>();
    public DbSet<User> Users => Set<User>();
    public DbSet<Customer> Customers => Set<Customer>();
    public DbSet<Notification> Notifications => Set<Notification>();
    public DbSet<Product> Products => Set<Product>();
    public DbSet<Store> Stores => Set<Store>();
    public DbSet<StoreInventory> StoreInventory => Set<StoreInventory>();
    public DbSet<Cart> Carts => Set<Cart>();
    public DbSet<CartItem> CartItems => Set<CartItem>();
    public DbSet<Order> Orders => Set<Order>();
    public DbSet<OrderItem> OrderItems => Set<OrderItem>();
    public DbSet<OrderStatusHistory> OrderStatusHistory => Set<OrderStatusHistory>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(QuickCommerceDbContext).Assembly);
        SeedData.Configure(modelBuilder);
    }
}
