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
        builder.Property(notification => notification.Category).HasMaxLength(20).HasDefaultValue(NotificationCategories.Order).IsRequired();
        builder.Property(notification => notification.DataJson).HasMaxLength(1000);
        // An offer reaches a customer once, even if two servers run the sending at the same moment.
        builder.HasIndex(notification => new { notification.CampaignId, notification.CustomerId }).IsUnique().HasFilter("[CampaignId] IS NOT NULL");
        builder.HasOne(notification => notification.Campaign)
            .WithMany()
            .HasForeignKey(notification => notification.CampaignId)
            .OnDelete(DeleteBehavior.Restrict);
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