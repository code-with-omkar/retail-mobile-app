using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using QuickCommerce.Domain;

namespace QuickCommerce.Infrastructure.Persistence.Configurations;

public sealed class OrderIssueConfiguration : IEntityTypeConfiguration<OrderIssue>
{
    public void Configure(EntityTypeBuilder<OrderIssue> builder)
    {
        builder.HasKey(issue => issue.Id);
        builder.Property(issue => issue.Kind).HasMaxLength(16).IsRequired();
        builder.Property(issue => issue.Status).HasMaxLength(12).IsRequired();
        builder.Property(issue => issue.Note).HasMaxLength(500);
        builder.Property(issue => issue.DecisionNote).HasMaxLength(500);
        builder.Property(issue => issue.RefundMethod).HasMaxLength(10);
        builder.Property(issue => issue.ClaimedAmount).HasPrecision(18, 2);
        builder.Property(issue => issue.ApprovedAmount).HasPrecision(18, 2);
        builder.Property(issue => issue.CreatedAt).HasColumnType("datetime2");
        builder.Property(issue => issue.DecidedAt).HasColumnType("datetime2");
        builder.Property(issue => issue.CashPaidAt).HasColumnType("datetime2");
        // One report for an order.
        builder.HasIndex(issue => issue.OrderId).IsUnique();
        builder.HasIndex(issue => new { issue.OrganizationId, issue.StoreId, issue.CreatedAt });
        builder.HasOne<Order>().WithMany().HasForeignKey(issue => issue.OrderId).OnDelete(DeleteBehavior.Cascade);
        builder.HasMany(issue => issue.Items).WithOne().HasForeignKey(item => item.IssueId).OnDelete(DeleteBehavior.Cascade);
        builder.ToTable(table => table.HasCheckConstraint("CK_OrderIssues_Amounts", "[ClaimedAmount] >= 0 AND ([ApprovedAmount] IS NULL OR [ApprovedAmount] >= 0)"));
    }
}

public sealed class OrderIssueItemConfiguration : IEntityTypeConfiguration<OrderIssueItem>
{
    public void Configure(EntityTypeBuilder<OrderIssueItem> builder)
    {
        builder.HasKey(item => item.Id);
        builder.Property(item => item.Name).HasMaxLength(200).IsRequired();
        builder.Property(item => item.Label).HasMaxLength(80);
        builder.Property(item => item.Amount).HasPrecision(18, 2);
    }
}
