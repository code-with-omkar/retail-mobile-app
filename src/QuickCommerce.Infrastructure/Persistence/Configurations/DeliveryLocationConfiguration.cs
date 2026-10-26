using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using QuickCommerce.Domain;

namespace QuickCommerce.Infrastructure.Persistence.Configurations;

public sealed class DeliveryLocationConfiguration : IEntityTypeConfiguration<DeliveryLocation>
{
    public void Configure(EntityTypeBuilder<DeliveryLocation> builder)
    {
        builder.HasKey(location => location.OrderId);
        builder.Property(location => location.UpdatedAt).HasColumnType("datetime2");
        builder.HasOne<Order>().WithOne().HasForeignKey<DeliveryLocation>(location => location.OrderId).OnDelete(DeleteBehavior.Cascade);
        builder.HasIndex(location => location.UpdatedAt);
        builder.ToTable(table => table.HasCheckConstraint("CK_DeliveryLocations_Range", "[Latitude] BETWEEN -90 AND 90 AND [Longitude] BETWEEN -180 AND 180"));
    }
}
