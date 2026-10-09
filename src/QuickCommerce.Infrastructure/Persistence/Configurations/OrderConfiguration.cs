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
        builder.Property(order => order.SubtotalAmount).HasPrecision(18, 2).IsRequired();
        builder.Property(order => order.DeliveryFee).HasPrecision(18, 2).IsRequired();
        builder.Property(order => order.HandlingFee).HasPrecision(18, 2).IsRequired();
        builder.Property(order => order.PaymentMethod).HasMaxLength(32).IsRequired().HasDefaultValue(PaymentMethods.CashOnDelivery);
        builder.Property(order => order.ReceiverName).HasMaxLength(120);
        builder.Property(order => order.ReceiverPhone).HasMaxLength(20);
        builder.Property(order => order.EstimatedDeliveryMinutes);
        builder.Property(order => order.DeliveryAddress).HasMaxLength(500).IsRequired();
        builder.Property(order => order.Status).HasConversion<string>().HasMaxLength(32).IsRequired();
        builder.Property(order => order.CreatedAt).HasColumnType("datetime2").IsRequired();
        builder.Property(order => order.RowVersion).IsRowVersion().IsConcurrencyToken();
        builder.HasIndex(order => order.OrderNumber).IsUnique();
        builder.ToTable(table => table.HasCheckConstraint("CK_Orders_Fees", "[DeliveryFee] >= 0 AND [HandlingFee] >= 0 AND [SubtotalAmount] >= 0"));
        builder.HasIndex(order => new { order.StoreId, order.CreatedAt });
        builder.HasOne<Store>().WithMany().HasForeignKey(order => order.StoreId).OnDelete(DeleteBehavior.Restrict);
        builder.HasMany(order => order.Items).WithOne().HasForeignKey("OrderId").OnDelete(DeleteBehavior.Cascade);
        builder.HasMany(order => order.StatusHistory).WithOne().HasForeignKey("OrderId").OnDelete(DeleteBehavior.Cascade);
    }
}
