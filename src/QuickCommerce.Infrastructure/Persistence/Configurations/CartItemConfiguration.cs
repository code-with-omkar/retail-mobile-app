using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using QuickCommerce.Domain;

namespace QuickCommerce.Infrastructure.Persistence.Configurations;

public sealed class CartItemConfiguration : IEntityTypeConfiguration<CartItem>
{
    public void Configure(EntityTypeBuilder<CartItem> builder)
    {
        // One line per variant: the same product in two pack sizes is two lines.
        builder.HasKey(item => new { item.CartId, item.VariantId });
        builder.Property(item => item.ProductNameSnapshot).HasMaxLength(160).IsRequired();
        builder.Property(item => item.VariantLabelSnapshot).HasMaxLength(40).IsRequired();
        builder.Property(item => item.UnitPriceSnapshot).HasPrecision(18, 2).IsRequired();
        builder.Property(item => item.AddedAt).HasColumnType("datetime2").IsRequired();
        builder.HasOne(item => item.Product)
            .WithMany()
            .HasForeignKey(item => item.ProductId)
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<ProductVariant>().WithMany().HasForeignKey(item => item.VariantId).OnDelete(DeleteBehavior.Restrict);
    }
}