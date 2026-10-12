using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using QuickCommerce.Domain;

namespace QuickCommerce.Infrastructure.Persistence.Configurations;

public sealed class PlacementBidConfiguration : IEntityTypeConfiguration<PlacementBid>
{
    public void Configure(EntityTypeBuilder<PlacementBid> builder)
    {
        builder.ToTable("PlacementBids");
        builder.HasKey(bid => bid.Id);
        builder.Property(bid => bid.Pincode).HasMaxLength(6).IsRequired();
        builder.Property(bid => bid.WeekStart).HasColumnType("date");
        builder.Property(bid => bid.Status).HasConversion<string>().HasMaxLength(20).IsRequired();
        builder.Property(bid => bid.PaymentStatus).HasConversion<string>().HasMaxLength(20).IsRequired();
        builder.Property(bid => bid.Provider).HasMaxLength(32).IsRequired();
        builder.Property(bid => bid.ProviderOrderId).HasMaxLength(64);
        builder.Property(bid => bid.ProviderPaymentId).HasMaxLength(64);
        builder.Property(bid => bid.Currency).HasMaxLength(3).IsRequired();
        builder.Property(bid => bid.FailureReason).HasMaxLength(200);
        builder.Property(bid => bid.RefundId).HasMaxLength(64);
        builder.Property(bid => bid.CreatedAt).HasColumnType("datetime2");
        builder.Property(bid => bid.UpdatedAt).HasColumnType("datetime2");
        builder.Property(bid => bid.RowVersion).IsRowVersion().IsConcurrencyToken();
        // Only one bid can lead a pincode in a week: the database itself refuses a second.
        builder.HasIndex(bid => new { bid.Pincode, bid.WeekStart }).IsUnique().HasFilter("[Status] = 'Leading'").HasDatabaseName("UX_PlacementBids_Leading");
        builder.HasIndex(bid => bid.ProviderOrderId).IsUnique().HasFilter("[ProviderOrderId] IS NOT NULL").HasDatabaseName("UX_PlacementBids_ProviderOrderId");
        builder.HasIndex(bid => bid.ProviderPaymentId).IsUnique().HasFilter("[ProviderPaymentId] IS NOT NULL").HasDatabaseName("UX_PlacementBids_ProviderPaymentId");
        builder.HasIndex(bid => new { bid.StoreId, bid.WeekStart });
        builder.HasIndex(bid => new { bid.PaymentStatus, bid.UpdatedAt });
        builder.ToTable(table => table.HasCheckConstraint("CK_PlacementBids_Amount", "[AmountPaise] > 0 AND [Attempts] >= 0"));
        builder.HasOne(bid => bid.Store).WithMany().HasForeignKey(bid => bid.StoreId).OnDelete(DeleteBehavior.Restrict);
    }
}

public sealed class PlacementRulesConfiguration : IEntityTypeConfiguration<PlacementRules>
{
    public void Configure(EntityTypeBuilder<PlacementRules> builder)
    {
        builder.ToTable("PlacementRules");
        builder.HasKey(rules => rules.Id);
        builder.Property(rules => rules.UpdatedAt).HasColumnType("datetime2");
        builder.Property(rules => rules.UpdatedBy).HasMaxLength(200);
        builder.ToTable(table => table.HasCheckConstraint("CK_PlacementRules_Values", "[MinBidPaise] > 0 AND [MinStepPaise] >= 0 AND [MaxWeeksAhead] BETWEEN 1 AND 26"));
    }
}
