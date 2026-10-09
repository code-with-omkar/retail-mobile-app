using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using QuickCommerce.Domain;

namespace QuickCommerce.Infrastructure.Persistence.Configurations;

public sealed class CategoryTranslationConfiguration : IEntityTypeConfiguration<CategoryTranslation>
{
    public void Configure(EntityTypeBuilder<CategoryTranslation> builder)
    {
        builder.ToTable("CategoryTranslations");
        builder.HasKey(translation => new { translation.CategoryId, translation.Locale });
        builder.Property(translation => translation.Locale).HasMaxLength(10).IsRequired();
        builder.Property(translation => translation.Name).HasMaxLength(120).IsRequired();
        builder.HasOne<Category>().WithMany().HasForeignKey(translation => translation.CategoryId).OnDelete(DeleteBehavior.Cascade);
    }
}
