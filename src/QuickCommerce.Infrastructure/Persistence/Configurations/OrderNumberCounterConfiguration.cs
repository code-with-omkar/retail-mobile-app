using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using QuickCommerce.Domain;

namespace QuickCommerce.Infrastructure.Persistence.Configurations;

public sealed class OrderNumberCounterConfiguration : IEntityTypeConfiguration<OrderNumberCounter>
{
    public void Configure(EntityTypeBuilder<OrderNumberCounter> builder)
    {
        builder.HasKey(counter => new { counter.StoreId, counter.Day });
        builder.Property(counter => counter.Day).HasColumnType("date");
        builder.HasOne<Store>().WithMany().HasForeignKey(counter => counter.StoreId).OnDelete(DeleteBehavior.Cascade);
        builder.ToTable(table => table.HasCheckConstraint("CK_OrderNumberCounters_LastNumber", "[LastNumber] >= 0"));
    }
}
