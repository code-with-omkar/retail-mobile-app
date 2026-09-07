using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using QuickCommerce.Domain;

namespace QuickCommerce.Infrastructure.Persistence.Configurations;

public sealed class OrderStatusHistoryConfiguration : IEntityTypeConfiguration<OrderStatusHistory>
{
    public void Configure(EntityTypeBuilder<OrderStatusHistory> builder)
    {
        builder.Property<Guid>("Id").HasDefaultValueSql("NEWSEQUENTIALID()");
        builder.Property<Guid>("OrderId").IsRequired();
        builder.HasKey("Id");
        builder.Property(history => history.Status).HasConversion<string>().HasMaxLength(32).IsRequired();
        builder.Property(history => history.ChangedAt).HasColumnType("datetime2").IsRequired();
    }
}
