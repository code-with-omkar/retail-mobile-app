using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using QuickCommerce.Domain;

namespace QuickCommerce.Infrastructure.Persistence.Configurations;

public sealed class CommissionRateConfiguration : IEntityTypeConfiguration<CommissionRate>
{
    public void Configure(EntityTypeBuilder<CommissionRate> builder)
    {
        builder.HasKey(rate => rate.Id);
        builder.Property(rate => rate.Percent).HasColumnType("decimal(5,2)");
        builder.Property(rate => rate.UpdatedAt).HasColumnType("datetime2");
        builder.Property(rate => rate.UpdatedBy).HasMaxLength(200);
        // One default row (no store) and one row per store: SQL Server's unique index allows a single NULL.
        builder.HasIndex(rate => rate.StoreId).IsUnique();
        builder.HasOne<Store>().WithMany().HasForeignKey(rate => rate.StoreId).OnDelete(DeleteBehavior.Cascade);
        builder.ToTable(table => table.HasCheckConstraint("CK_CommissionRates_Percent", "[Percent] >= 0 AND [Percent] <= 100"));
    }
}
