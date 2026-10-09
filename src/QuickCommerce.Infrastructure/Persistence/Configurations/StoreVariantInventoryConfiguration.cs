using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using QuickCommerce.Domain;

namespace QuickCommerce.Infrastructure.Persistence.Configurations;

public sealed class StoreVariantInventoryConfiguration : IEntityTypeConfiguration<StoreVariantInventory>
{
    public void Configure(EntityTypeBuilder<StoreVariantInventory> builder)
    {
        builder.HasKey(inventory => new { inventory.StoreId, inventory.VariantId });
        builder.Property(inventory => inventory.AvailableQuantity).IsRequired();
        builder.Property(inventory => inventory.ReorderThreshold).IsRequired();
        builder.Property(inventory => inventory.RowVersion).IsRowVersion().IsConcurrencyToken();
        builder.ToTable(table => table.HasCheckConstraint("CK_StoreVariantInventory_NotNegative", "[AvailableQuantity] >= 0"));
        builder.HasOne<Store>().WithMany().HasForeignKey(inventory => inventory.StoreId).OnDelete(DeleteBehavior.Cascade);
        builder.HasOne<ProductVariant>().WithMany().HasForeignKey(inventory => inventory.VariantId).OnDelete(DeleteBehavior.Restrict);
    }
}
