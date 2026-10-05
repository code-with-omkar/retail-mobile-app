using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using QuickCommerce.Domain;

namespace QuickCommerce.Infrastructure.Persistence.Configurations;

public sealed class ApprovalRequestConfiguration : IEntityTypeConfiguration<ApprovalRequest>
{
    public void Configure(EntityTypeBuilder<ApprovalRequest> builder)
    {
        builder.HasKey(request => request.Id);
        builder.Property(request => request.EntityType).HasMaxLength(80).IsRequired();
        builder.Property(request => request.ApprovalType).HasMaxLength(80).IsRequired();
        builder.Property(request => request.Status).HasConversion<string>().HasMaxLength(32).IsRequired();
        builder.Property(request => request.RejectionReason).HasMaxLength(1000);
        builder.Property(request => request.CreatedBy).HasMaxLength(200);
        builder.Property(request => request.UpdatedBy).HasMaxLength(200);
        builder.HasIndex(request => new { request.OrganizationId, request.StoreId, request.Status, request.CreatedAt });
        builder.HasOne(request => request.Organization).WithMany().HasForeignKey(request => request.OrganizationId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(request => request.Store).WithMany().HasForeignKey(request => request.StoreId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(request => request.RequestedByUser).WithMany().HasForeignKey(request => request.RequestedByUserId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(request => request.ApprovedByUser).WithMany().HasForeignKey(request => request.ApprovedByUserId).OnDelete(DeleteBehavior.Restrict);
    }
}