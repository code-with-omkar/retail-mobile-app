using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using QuickCommerce.Domain;

namespace QuickCommerce.Infrastructure.Persistence.Configurations;

public sealed class ProductConfiguration : IEntityTypeConfiguration<Product>
{
    public void Configure(EntityTypeBuilder<Product> builder)
    {
        builder.HasKey(product => product.Id);
        builder.Property(product => product.Sku).HasMaxLength(64).IsRequired();
        builder.Property(product => product.Name).HasMaxLength(160).IsRequired();
        builder.Property(product => product.Description).HasMaxLength(2000).IsRequired();
        builder.Property(product => product.Price).HasPrecision(18, 2).IsRequired();
        builder.Property(product => product.UnitOfMeasure).HasMaxLength(32).IsRequired();
        builder.Property(product => product.ImageUrl).HasMaxLength(500);
        builder.HasIndex(product => product.Sku).IsUnique();
        builder.HasIndex(product => new { product.CategoryId, product.IsActive });
        builder.HasOne<Category>().WithMany().HasForeignKey(product => product.CategoryId).OnDelete(DeleteBehavior.Restrict);
    }
}
