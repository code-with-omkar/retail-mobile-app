using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using QuickCommerce.Domain;

namespace QuickCommerce.Infrastructure.Persistence.Configurations;

public sealed class OrderItemConfiguration : IEntityTypeConfiguration<OrderItem>
{
    public void Configure(EntityTypeBuilder<OrderItem> builder)
    {
        builder.Property<Guid>("Id").HasDefaultValueSql("NEWSEQUENTIALID()");
        builder.Property<Guid>("OrderId").IsRequired();
        builder.HasKey("Id");
        builder.Property(item => item.ProductNameSnapshot).HasMaxLength(160).IsRequired();
        builder.Property(item => item.VariantLabelSnapshot).HasMaxLength(40);
        builder.Property(item => item.UnitPrice).HasPrecision(18, 2).IsRequired();
        builder.Property(item => item.UnitMrpSnapshot).HasPrecision(18, 2);
        builder.Property(item => item.Quantity).IsRequired();
        builder.HasOne<Product>().WithMany().HasForeignKey(item => item.ProductId).OnDelete(DeleteBehavior.Restrict);
        // Null for orders placed before variants existed.
        builder.HasOne<ProductVariant>().WithMany().HasForeignKey(item => item.VariantId).OnDelete(DeleteBehavior.Restrict);
    }
}
