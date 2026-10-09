using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using QuickCommerce.Domain;

namespace QuickCommerce.Infrastructure.Persistence.Configurations;

public sealed class PaymentConfiguration : IEntityTypeConfiguration<Payment>
{
    public void Configure(EntityTypeBuilder<Payment> builder)
    {
        builder.ToTable("Payments");
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
        // One payment per order, and one order or payment id from the provider only ever belongs to one payment.
        builder.HasIndex(payment => payment.OrderId).IsUnique();
        builder.HasIndex(payment => payment.ProviderOrderId).IsUnique().HasFilter("[ProviderOrderId] IS NOT NULL").HasDatabaseName("UX_Payments_ProviderOrderId");
        builder.HasIndex(payment => payment.ProviderPaymentId).IsUnique().HasFilter("[ProviderPaymentId] IS NOT NULL").HasDatabaseName("UX_Payments_ProviderPaymentId");
        builder.HasIndex(payment => new { payment.Status, payment.UpdatedAt });
        builder.ToTable(table => table.HasCheckConstraint("CK_Payments_Amount", "[AmountPaise] > 0 AND [Attempts] >= 0"));
        builder.HasOne<Order>().WithMany().HasForeignKey(payment => payment.OrderId).OnDelete(DeleteBehavior.Cascade);
    }
}

public sealed class PaymentEventConfiguration : IEntityTypeConfiguration<PaymentEvent>
{
    public void Configure(EntityTypeBuilder<PaymentEvent> builder)
    {
        builder.ToTable("PaymentEvents");
        builder.HasKey(item => item.Id);
        builder.Property(item => item.Provider).HasMaxLength(32).IsRequired();
        builder.Property(item => item.EventId).HasMaxLength(100).IsRequired();
        builder.Property(item => item.Type).HasMaxLength(64).IsRequired();
        builder.Property(item => item.ReceivedAt).HasColumnType("datetime2");
        builder.HasIndex(item => new { item.Provider, item.EventId }).IsUnique().HasDatabaseName("UX_PaymentEvents_ProviderEvent");
    }
}
