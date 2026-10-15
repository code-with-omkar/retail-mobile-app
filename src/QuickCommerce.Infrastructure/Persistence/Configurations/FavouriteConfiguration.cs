using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using QuickCommerce.Domain;

namespace QuickCommerce.Infrastructure.Persistence.Configurations;

public sealed class FavouriteConfiguration : IEntityTypeConfiguration<FavouriteProduct>
{
    public void Configure(EntityTypeBuilder<FavouriteProduct> builder)
    {
        builder.ToTable("FavouriteProducts");
        builder.HasKey(item => new { item.CustomerId, item.ProductId });
        builder.Property(item => item.CreatedAt).HasColumnType("datetime2");
        builder.HasIndex(item => new { item.CustomerId, item.CreatedAt });
        builder.HasOne<Customer>().WithMany().HasForeignKey(item => item.CustomerId).OnDelete(DeleteBehavior.Cascade);
        builder.HasOne<Product>().WithMany().HasForeignKey(item => item.ProductId).OnDelete(DeleteBehavior.Restrict);
    }
}
