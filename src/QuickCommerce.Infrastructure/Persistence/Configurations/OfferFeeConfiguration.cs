using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using QuickCommerce.Domain;

namespace QuickCommerce.Infrastructure.Persistence.Configurations;

public sealed class OfferFeeConfiguration : IEntityTypeConfiguration<OfferFee>
{
    public void Configure(EntityTypeBuilder<OfferFee> builder)
    {
        builder.HasKey(fee => fee.Id);
        builder.Property(fee => fee.LoadAmountPaise).HasDefaultValue(0);
        builder.Property(fee => fee.UpdatedAt).HasColumnType("datetime2");
        builder.Property(fee => fee.UpdatedBy).HasMaxLength(200);
        // One default row (no store) and one row per store: SQL Server's unique index allows a single NULL.
        builder.HasIndex(fee => fee.StoreId).IsUnique();
        builder.HasOne<Store>().WithMany().HasForeignKey(fee => fee.StoreId).OnDelete(DeleteBehavior.Cascade);
        builder.ToTable(table => table.HasCheckConstraint("CK_OfferFees_Amount", "[AmountPaise] >= 0"));
    }
}

public sealed class CampaignPaymentConfiguration : IEntityTypeConfiguration<CampaignPayment>
{
    public void Configure(EntityTypeBuilder<CampaignPayment> builder)
    {
        builder.ToTable("CampaignPayments");
        builder.HasKey(payment => payment.Id);
        builder.Property(payment => payment.Provider).HasMaxLength(32).IsRequired();
        builder.Property(payment => payment.ProviderOrderId).HasMaxLength(64);
        builder.Property(payment => payment.ProviderPaymentId).HasMaxLength(64);
        builder.Property(payment => payment.Currency).HasMaxLength(3).IsRequired();
        builder.Property(payment => payment.Status).HasConversion<string>().HasMaxLength(20).IsRequired();
        builder.Property(payment => payment.FailureReason).HasMaxLength(200);
        builder.Property(payment => payment.RefundId).HasMaxLength(64);
        builder.Property(payment => payment.CreatedAt).HasColumnType("datetime2");
        builder.Property(payment => payment.UpdatedAt).HasColumnType("datetime2");
        builder.Property(payment => payment.RowVersion).IsRowVersion().IsConcurrencyToken();
        builder.HasIndex(payment => payment.CampaignId).IsUnique();
        builder.HasIndex(payment => payment.ProviderOrderId).IsUnique().HasFilter("[ProviderOrderId] IS NOT NULL").HasDatabaseName("UX_CampaignPayments_ProviderOrderId");
        builder.HasIndex(payment => payment.ProviderPaymentId).IsUnique().HasFilter("[ProviderPaymentId] IS NOT NULL").HasDatabaseName("UX_CampaignPayments_ProviderPaymentId");
        builder.HasIndex(payment => new { payment.Status, payment.UpdatedAt });
        builder.ToTable(table => table.HasCheckConstraint("CK_CampaignPayments_Amount", "[AmountPaise] > 0 AND [Attempts] >= 0"));
        builder.HasOne<Campaign>().WithMany().HasForeignKey(payment => payment.CampaignId).OnDelete(DeleteBehavior.Cascade);
    }
}
