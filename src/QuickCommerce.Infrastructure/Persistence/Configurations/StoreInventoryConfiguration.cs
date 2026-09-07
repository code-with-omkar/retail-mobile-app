using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using QuickCommerce.Domain;

namespace QuickCommerce.Infrastructure.Persistence.Configurations;

public sealed class StoreInventoryConfiguration : IEntityTypeConfiguration<StoreInventory>
{
    public void Configure(EntityTypeBuilder<StoreInventory> builder)
    {
        builder.HasKey(inventory => new { inventory.StoreId, inventory.ProductId });
        builder.Property(inventory => inventory.AvailableQuantity).IsRequired();
        builder.Property(inventory => inventory.ReorderThreshold).IsRequired();
        builder.Property(inventory => inventory.RowVersion).IsRowVersion().IsConcurrencyToken();
        builder.HasOne<Store>().WithMany().HasForeignKey(inventory => inventory.StoreId).OnDelete(DeleteBehavior.Cascade);
        builder.HasOne<Product>().WithMany().HasForeignKey(inventory => inventory.ProductId).OnDelete(DeleteBehavior.Restrict);
    }
}
