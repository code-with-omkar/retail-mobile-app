using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using QuickCommerce.Domain;

namespace QuickCommerce.Infrastructure.Persistence.Configurations;

public sealed class AdConfiguration : IEntityTypeConfiguration<Ad>
{
    public void Configure(EntityTypeBuilder<Ad> builder)
    {
        builder.HasKey(ad => ad.Id);
        builder.Property(ad => ad.Placement).HasMaxLength(20).IsRequired();
        builder.Property(ad => ad.Title).HasMaxLength(80);
        builder.Property(ad => ad.ImageUrl).HasMaxLength(200).IsRequired();
        builder.Property(ad => ad.TargetType).HasMaxLength(10).IsRequired();
        builder.Property(ad => ad.Target).HasMaxLength(300);
        builder.Property(ad => ad.StartsAt).HasColumnType("datetime2");
        builder.Property(ad => ad.EndsAt).HasColumnType("datetime2");
        builder.Property(ad => ad.CreatedAt).HasColumnType("datetime2");
        builder.Property(ad => ad.UpdatedAt).HasColumnType("datetime2");
        builder.Property(ad => ad.CreatedBy).HasMaxLength(200);
        builder.HasIndex(ad => new { ad.Placement, ad.IsActive });
        builder.HasIndex(ad => ad.OrganizationId);
        builder.HasOne<Organization>().WithMany().HasForeignKey(ad => ad.OrganizationId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<Store>().WithMany().HasForeignKey(ad => ad.StoreId).OnDelete(DeleteBehavior.Cascade);
        builder.ToTable(table =>
        {
            table.HasCheckConstraint("CK_Ads_Placement", "[Placement] IN ('home','home_middle','categories','search','cart','order','product')");
            table.HasCheckConstraint("CK_Ads_TargetType", "[TargetType] IN ('None','Product','Category','Offer','Url')");
            table.HasCheckConstraint("CK_Ads_Counts", "[ViewCount] >= 0 AND [ClickCount] >= 0");
        });
    }
}
