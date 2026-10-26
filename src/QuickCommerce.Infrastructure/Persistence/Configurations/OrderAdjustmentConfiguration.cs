using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using QuickCommerce.Domain;

namespace QuickCommerce.Infrastructure.Persistence.Configurations;

public sealed class OrderAdjustmentConfiguration : IEntityTypeConfiguration<OrderAdjustment>
{
    public void Configure(EntityTypeBuilder<OrderAdjustment> builder)
    {
        builder.HasKey(adjustment => adjustment.Id);
        builder.Property(adjustment => adjustment.Reason).HasMaxLength(200);
        builder.Property(adjustment => adjustment.ChangesJson).HasMaxLength(4000).IsRequired();
        builder.Property(adjustment => adjustment.OldSubtotal).HasPrecision(18, 2);
        builder.Property(adjustment => adjustment.NewSubtotal).HasPrecision(18, 2);
        builder.Property(adjustment => adjustment.RefundStatus).HasMaxLength(12).IsRequired();
        builder.Property(adjustment => adjustment.RefundId).HasMaxLength(64);
        builder.Property(adjustment => adjustment.RefundFailure).HasMaxLength(200);
        builder.Property(adjustment => adjustment.CreatedAt).HasColumnType("datetime2");
        builder.HasOne<Order>().WithMany().HasForeignKey(adjustment => adjustment.OrderId).OnDelete(DeleteBehavior.Cascade);
        builder.HasIndex(adjustment => adjustment.OrderId);
        // The refund sender looks for refunds that are waiting or started.
        builder.HasIndex(adjustment => adjustment.RefundStatus).HasFilter("[RefundStatus] IN ('Pending', 'Started')");
        builder.ToTable(table => table.HasCheckConstraint("CK_OrderAdjustments_Refund", "[RefundPaise] >= 0 AND [NewSubtotal] >= 0 AND [NewSubtotal] <= [OldSubtotal]"));
    }
}
