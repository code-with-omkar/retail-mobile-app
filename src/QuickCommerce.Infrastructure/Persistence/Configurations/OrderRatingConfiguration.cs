using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using QuickCommerce.Domain;

namespace QuickCommerce.Infrastructure.Persistence.Configurations;

public sealed class OrderRatingConfiguration : IEntityTypeConfiguration<OrderRating>
{
    public void Configure(EntityTypeBuilder<OrderRating> builder)
    {
        builder.ToTable("OrderRatings");
        builder.HasKey(rating => rating.OrderId);
        builder.Property(rating => rating.Comment).HasMaxLength(500);
        builder.Property(rating => rating.CreatedAt).HasColumnType("datetime2");
        builder.Property(rating => rating.UpdatedAt).HasColumnType("datetime2");
        builder.HasIndex(rating => new { rating.StoreId, rating.CreatedAt });
        builder.HasIndex(rating => new { rating.DeliveryPartnerId, rating.CreatedAt }).HasFilter("[DeliveryPartnerId] IS NOT NULL");
        builder.ToTable(table =>
        {
            table.HasCheckConstraint("CK_OrderRatings_StoreStars", "[StoreStars] BETWEEN 1 AND 5");
            table.HasCheckConstraint("CK_OrderRatings_DeliveryStars", "[DeliveryStars] IS NULL OR [DeliveryStars] BETWEEN 1 AND 5");
        });
        builder.HasOne<Order>().WithOne().HasForeignKey<OrderRating>(rating => rating.OrderId).OnDelete(DeleteBehavior.Restrict);
    }
}
