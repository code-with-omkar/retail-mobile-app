using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using QuickCommerce.Domain;

namespace QuickCommerce.Infrastructure.Persistence.Configurations;

public sealed class CampaignConfiguration : IEntityTypeConfiguration<Campaign>
{
    public void Configure(EntityTypeBuilder<Campaign> builder)
    {
        builder.HasKey(campaign => campaign.Id);
        builder.Property(campaign => campaign.TitleEn).HasMaxLength(160).IsRequired();
        builder.Property(campaign => campaign.BodyEn).HasMaxLength(500).IsRequired();
        builder.Property(campaign => campaign.TitleMr).HasMaxLength(160);
        builder.Property(campaign => campaign.BodyMr).HasMaxLength(500);
        builder.Property(campaign => campaign.StartsAt).HasColumnType("datetime2").IsRequired();
        builder.Property(campaign => campaign.PublishedAt).HasColumnType("datetime2");
        builder.Property(campaign => campaign.CreatedAt).HasColumnType("datetime2").IsRequired();
        builder.Property(campaign => campaign.CreatedBy).HasMaxLength(200);
        // The sender looks for campaigns that are due and not sent yet.
        builder.HasIndex(campaign => campaign.StartsAt).HasFilter("[PublishedAt] IS NULL AND [IsActive] = 1");
        builder.ToTable(table => table.HasCheckConstraint("CK_Campaigns_RecipientCount", "[RecipientCount] >= 0"));
    }
}
