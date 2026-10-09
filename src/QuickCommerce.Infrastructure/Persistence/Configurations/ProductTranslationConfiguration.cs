using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using QuickCommerce.Domain;

namespace QuickCommerce.Infrastructure.Persistence.Configurations;

public sealed class ProductTranslationConfiguration : IEntityTypeConfiguration<ProductTranslation>
{
    public void Configure(EntityTypeBuilder<ProductTranslation> builder)
    {
        builder.ToTable("ProductTranslations");
        builder.HasKey(translation => new { translation.ProductId, translation.Locale });
        builder.Property(translation => translation.Locale).HasMaxLength(10).IsRequired();
        builder.Property(translation => translation.Name).HasMaxLength(160).IsRequired();
        builder.Property(translation => translation.Description).HasMaxLength(2000);
        builder.HasOne<Product>().WithMany().HasForeignKey(translation => translation.ProductId).OnDelete(DeleteBehavior.Cascade);
    }
}
