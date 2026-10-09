using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using QuickCommerce.Domain;

namespace QuickCommerce.Infrastructure.Persistence.Configurations;

public sealed class CheckoutRequestRecordConfiguration : IEntityTypeConfiguration<CheckoutRequestRecord>
{
    public void Configure(EntityTypeBuilder<CheckoutRequestRecord> builder)
    {
        builder.ToTable("CheckoutRequests");
        builder.HasKey(record => record.Id);
        builder.Property(record => record.IdempotencyKey).HasMaxLength(64).IsRequired();
        builder.Property(record => record.RequestHash).HasMaxLength(64).IsRequired();
        builder.Property(record => record.CreatedAt).HasColumnType("datetime2");
        // One order per key and customer, whatever two parallel requests do.
        builder.HasIndex(record => new { record.CustomerId, record.IdempotencyKey }).IsUnique().HasDatabaseName("UX_CheckoutRequests_CustomerKey");
        builder.HasIndex(record => record.CreatedAt);
        builder.HasOne<Customer>().WithMany().HasForeignKey(record => record.CustomerId).OnDelete(DeleteBehavior.Cascade);
        builder.HasOne<Order>().WithMany().HasForeignKey(record => record.OrderId).OnDelete(DeleteBehavior.Cascade);
    }
}
