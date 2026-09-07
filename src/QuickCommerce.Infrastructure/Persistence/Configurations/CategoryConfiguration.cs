using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using QuickCommerce.Domain;

namespace QuickCommerce.Infrastructure.Persistence.Configurations;

public sealed class CategoryConfiguration : IEntityTypeConfiguration<Category>
{
    public void Configure(EntityTypeBuilder<Category> builder)
    {
        builder.HasKey(category => category.Id);
        builder.Property(category => category.Name).HasMaxLength(120).IsRequired();
        builder.HasIndex(category => category.Name).IsUnique();
        builder.HasOne<Category>().WithMany().HasForeignKey(category => category.ParentCategoryId).OnDelete(DeleteBehavior.Restrict);
    }
}
