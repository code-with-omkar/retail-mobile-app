using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using QuickCommerce.Domain;

namespace QuickCommerce.Infrastructure.Persistence.Configurations;

public sealed class DeliverySlotSettingConfiguration : IEntityTypeConfiguration<DeliverySlotSetting>
{
    public void Configure(EntityTypeBuilder<DeliverySlotSetting> builder)
    {
        builder.HasKey(row => row.Id);
        builder.Property(row => row.UpdatedAt).HasColumnType("datetime2");
        builder.Property(row => row.UpdatedBy).HasMaxLength(200);
        // One default row (no store) and one row per store: SQL Server's unique index allows a single NULL.
        builder.HasIndex(row => row.StoreId).IsUnique();
        builder.HasOne<Store>().WithMany().HasForeignKey(row => row.StoreId).OnDelete(DeleteBehavior.Cascade);
        builder.ToTable(table => table.HasCheckConstraint("CK_DeliverySlotSettings_Ranges",
            "[OpenMinutes] >= 0 AND [CloseMinutes] <= 1440 AND [CloseMinutes] > [OpenMinutes] AND [WindowMinutes] > 0 AND [Capacity] >= 1 AND [CutoffMinutes] >= 0 AND [DaysAhead] BETWEEN 1 AND 7"));
    }
}

public sealed class SlotBookingConfiguration : IEntityTypeConfiguration<SlotBooking>
{
    public void Configure(EntityTypeBuilder<SlotBooking> builder)
    {
        builder.HasKey(row => new { row.StoreId, row.SlotStart });
        builder.Property(row => row.SlotStart).HasColumnType("datetime2");
        builder.HasOne<Store>().WithMany().HasForeignKey(row => row.StoreId).OnDelete(DeleteBehavior.Cascade);
        builder.ToTable(table => table.HasCheckConstraint("CK_SlotBookings_Booked", "[Booked] >= 0"));
    }
}
