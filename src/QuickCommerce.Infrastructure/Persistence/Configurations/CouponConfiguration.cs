using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using QuickCommerce.Domain;

namespace QuickCommerce.Infrastructure.Persistence.Configurations;

public sealed class CouponConfiguration : IEntityTypeConfiguration<Coupon>
{
    public void Configure(EntityTypeBuilder<Coupon> builder)
    {
        builder.HasKey(coupon => coupon.Id);
        builder.Property(coupon => coupon.Code).HasMaxLength(20).IsRequired();
        builder.Property(coupon => coupon.Description).HasMaxLength(200);
        builder.Property(coupon => coupon.Kind).HasMaxLength(16).IsRequired();
        builder.Property(coupon => coupon.Value).HasPrecision(18, 2);
        builder.Property(coupon => coupon.MaxDiscount).HasPrecision(18, 2);
        builder.Property(coupon => coupon.MinSubtotal).HasPrecision(18, 2);
        builder.Property(coupon => coupon.StartsAt).HasColumnType("datetime2");
        builder.Property(coupon => coupon.EndsAt).HasColumnType("datetime2");
        builder.Property(coupon => coupon.CreatedAt).HasColumnType("datetime2");
        builder.Property(coupon => coupon.CreatedBy).HasMaxLength(200);
        builder.Property(coupon => coupon.Design).HasMaxLength(20);
        builder.Property(coupon => coupon.ImageUrl).HasMaxLength(200);
        builder.Property(coupon => coupon.Ribbon).HasMaxLength(20);
        builder.Property(coupon => coupon.Animation).HasMaxLength(20);
        builder.HasIndex(coupon => new { coupon.OrganizationId, coupon.Code }).IsUnique();
        builder.HasOne<Organization>().WithMany().HasForeignKey(coupon => coupon.OrganizationId).OnDelete(DeleteBehavior.Restrict);
        builder.ToTable(table =>
        {
            table.HasCheckConstraint("CK_Coupons_Kind", "[Kind] IN ('Percent','Flat','FreeDelivery')");
            table.HasCheckConstraint("CK_Coupons_Amounts", "[Value] >= 0 AND [MinSubtotal] >= 0 AND ([MaxDiscount] IS NULL OR [MaxDiscount] > 0) AND [UsedCount] >= 0");
        });
    }
}

public sealed class CouponRedemptionConfiguration : IEntityTypeConfiguration<CouponRedemption>
{
    public void Configure(EntityTypeBuilder<CouponRedemption> builder)
    {
        builder.HasKey(redemption => redemption.Id);
        builder.Property(redemption => redemption.Kind).HasMaxLength(16).IsRequired();
        builder.Property(redemption => redemption.Value).HasPrecision(18, 2);
        builder.Property(redemption => redemption.MaxDiscount).HasPrecision(18, 2);
        builder.Property(redemption => redemption.Amount).HasPrecision(18, 2);
        builder.Property(redemption => redemption.CreatedAt).HasColumnType("datetime2");
        builder.Property(redemption => redemption.ReleasedAt).HasColumnType("datetime2");
        // One coupon per order.
        builder.HasIndex(redemption => redemption.OrderId).IsUnique();
        builder.HasIndex(redemption => new { redemption.CouponId, redemption.UserId });
        builder.HasOne<Coupon>().WithMany().HasForeignKey(redemption => redemption.CouponId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<Order>().WithMany().HasForeignKey(redemption => redemption.OrderId).OnDelete(DeleteBehavior.Cascade);
        builder.ToTable(table => table.HasCheckConstraint("CK_CouponRedemptions_Amount", "[Amount] >= 0"));
    }
}
