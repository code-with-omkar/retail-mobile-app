using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using QuickCommerce.Domain;

namespace QuickCommerce.Infrastructure.Persistence.Configurations;

public sealed class CampaignStoreConfiguration : IEntityTypeConfiguration<CampaignStore>
{
    public void Configure(EntityTypeBuilder<CampaignStore> builder)
    {
        builder.HasKey(link => new { link.CampaignId, link.StoreId });
        builder.HasOne<Campaign>().WithMany(campaign => campaign.Stores).HasForeignKey(link => link.CampaignId).OnDelete(DeleteBehavior.Cascade);
        builder.HasOne(link => link.Store).WithMany().HasForeignKey(link => link.StoreId).OnDelete(DeleteBehavior.Restrict);
        builder.HasIndex(link => link.StoreId);
    }
}
