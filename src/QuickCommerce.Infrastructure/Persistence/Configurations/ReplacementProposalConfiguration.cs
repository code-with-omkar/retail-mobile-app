using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using QuickCommerce.Domain;

namespace QuickCommerce.Infrastructure.Persistence.Configurations;

public sealed class ReplacementProposalConfiguration : IEntityTypeConfiguration<ReplacementProposal>
{
    public void Configure(EntityTypeBuilder<ReplacementProposal> builder)
    {
        builder.HasKey(proposal => proposal.Id);
        builder.Property(proposal => proposal.LineName).HasMaxLength(160).IsRequired();
        builder.Property(proposal => proposal.LineLabel).HasMaxLength(40);
        builder.Property(proposal => proposal.NewName).HasMaxLength(160).IsRequired();
        builder.Property(proposal => proposal.NewLabel).HasMaxLength(40);
        builder.Property(proposal => proposal.OldUnitPrice).HasPrecision(18, 2);
        builder.Property(proposal => proposal.NewUnitPrice).HasPrecision(18, 2);
        builder.Property(proposal => proposal.NewUnitMrp).HasPrecision(18, 2);
        builder.Property(proposal => proposal.Status).HasMaxLength(10).IsRequired();
        builder.Property(proposal => proposal.CreatedAt).HasColumnType("datetime2");
        builder.Property(proposal => proposal.ExpiresAt).HasColumnType("datetime2");
        builder.Property(proposal => proposal.DecidedAt).HasColumnType("datetime2");
        builder.HasOne<Order>().WithMany().HasForeignKey(proposal => proposal.OrderId).OnDelete(DeleteBehavior.Cascade);
        builder.HasIndex(proposal => proposal.OrderId);
        // One open proposal per order at a time.
        builder.HasIndex(proposal => proposal.OrderId).IsUnique().HasDatabaseName("UX_ReplacementProposals_OnePending").HasFilter("[Status] = 'Pending'");
        builder.HasIndex(proposal => proposal.ExpiresAt).HasFilter("[Status] = 'Pending'");
        builder.ToTable(table => table.HasCheckConstraint("CK_ReplacementProposals_Quantity", "[OldQuantity] > 0 AND [NewQuantity] > 0 AND [NewUnitPrice] >= 0"));
    }
}
