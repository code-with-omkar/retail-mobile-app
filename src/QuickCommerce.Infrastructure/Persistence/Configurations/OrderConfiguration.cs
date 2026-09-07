using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using QuickCommerce.Domain;

namespace QuickCommerce.Infrastructure.Persistence.Configurations;

public sealed class OrderConfiguration : IEntityTypeConfiguration<Order>
{
    public void Configure(EntityTypeBuilder<Order> builder)
    {
        builder.HasKey(order => order.Id);
        builder.Property(order => order.OrderNumber).HasMaxLength(32).IsRequired();
        builder.Property(order => order.TotalAmount).HasPrecision(18, 2).IsRequired();
        builder.Property(order => order.DeliveryAddress).HasMaxLength(500).IsRequired();
        builder.Property(order => order.Status).HasConversion<string>().HasMaxLength(32).IsRequired();
        builder.Property(order => order.CreatedAt).HasColumnType("datetime2").IsRequired();
        builder.Property(order => order.RowVersion).IsRowVersion().IsConcurrencyToken();
        builder.HasIndex(order => order.OrderNumber).IsUnique();
        builder.HasIndex(order => new { order.StoreId, order.CreatedAt });
        builder.HasOne<Store>().WithMany().HasForeignKey(order => order.StoreId).OnDelete(DeleteBehavior.Restrict);
        builder.HasMany(order => order.Items).WithOne().HasForeignKey("OrderId").OnDelete(DeleteBehavior.Cascade);
        builder.HasMany(order => order.StatusHistory).WithOne().HasForeignKey("OrderId").OnDelete(DeleteBehavior.Cascade);
    }
}
