using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using QuickCommerce.Domain;

namespace QuickCommerce.Infrastructure.Persistence.Configurations;

public sealed class CartItemConfiguration : IEntityTypeConfiguration<CartItem>
{
    public void Configure(EntityTypeBuilder<CartItem> builder)
    {
        builder.HasKey(item => new { item.CartId, item.ProductId });
        builder.Property(item => item.ProductNameSnapshot).HasMaxLength(160).IsRequired();
        builder.Property(item => item.UnitPriceSnapshot).HasPrecision(18, 2).IsRequired();
        builder.Property(item => item.AddedAt).HasColumnType("datetime2").IsRequired();
        builder.HasOne(item => item.Product)
            .WithMany()
            .HasForeignKey(item => item.ProductId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}