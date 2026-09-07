using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using QuickCommerce.Domain;

namespace QuickCommerce.Infrastructure.Persistence.Configurations;

public sealed class NotificationConfiguration : IEntityTypeConfiguration<Notification>
{
    public void Configure(EntityTypeBuilder<Notification> builder)
    {
        builder.HasKey(notification => notification.Id);
        builder.Property(notification => notification.Type).HasMaxLength(64).IsRequired();
        builder.Property(notification => notification.Title).HasMaxLength(160).IsRequired();
        builder.Property(notification => notification.Message).HasMaxLength(500).IsRequired();
        builder.Property(notification => notification.CreatedAt).HasColumnType("datetime2").IsRequired();
        builder.HasIndex(notification => new { notification.CustomerId, notification.IsRead, notification.CreatedAt });
        builder.HasOne(notification => notification.Customer)
            .WithMany(customer => customer.Notifications)
            .HasForeignKey(notification => notification.CustomerId)
            .OnDelete(DeleteBehavior.Cascade);
        builder.HasOne<Order>()
            .WithMany()
            .HasForeignKey(notification => notification.OrderId)
            .OnDelete(DeleteBehavior.SetNull);
    }
}