using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using QuickCommerce.Domain;

namespace QuickCommerce.Infrastructure.Persistence.Configurations;

public sealed class ProductVariantConfiguration : IEntityTypeConfiguration<ProductVariant>
{
    public void Configure(EntityTypeBuilder<ProductVariant> builder)
    {
        builder.HasKey(variant => variant.Id);
        builder.Property(variant => variant.Sku).HasMaxLength(80).IsRequired();
        builder.Property(variant => variant.Label).HasMaxLength(40).IsRequired();
        builder.Property(variant => variant.Price).HasPrecision(18, 2).IsRequired();
        builder.Property(variant => variant.Mrp).HasPrecision(18, 2);
        builder.HasIndex(variant => variant.Sku).IsUnique();
        // Exactly one default variant per product.
        builder.HasIndex(variant => variant.ProductId).IsUnique().HasFilter("[IsDefault] = 1").HasDatabaseName("UX_ProductVariants_OneDefaultPerProduct");
        builder.HasIndex(variant => new { variant.ProductId, variant.SortOrder });
        builder.ToTable(table => table.HasCheckConstraint("CK_ProductVariants_MrpAtLeastPrice", "[Mrp] IS NULL OR [Mrp] >= [Price]"));
        builder.HasOne<Product>().WithMany().HasForeignKey(variant => variant.ProductId).OnDelete(DeleteBehavior.Restrict);
    }
}
