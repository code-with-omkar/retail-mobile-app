using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using QuickCommerce.Domain;

namespace QuickCommerce.Infrastructure.Persistence.Configurations;

public sealed class CampaignProductConfiguration : IEntityTypeConfiguration<CampaignProduct>
{
    public void Configure(EntityTypeBuilder<CampaignProduct> builder)
    {
        builder.HasKey(link => new { link.CampaignId, link.ProductId });
        builder.HasOne<Campaign>().WithMany(campaign => campaign.Products).HasForeignKey(link => link.CampaignId).OnDelete(DeleteBehavior.Cascade);
        builder.HasOne<Product>().WithMany().HasForeignKey(link => link.ProductId).OnDelete(DeleteBehavior.Restrict);
        builder.HasIndex(link => link.ProductId);
    }
}
