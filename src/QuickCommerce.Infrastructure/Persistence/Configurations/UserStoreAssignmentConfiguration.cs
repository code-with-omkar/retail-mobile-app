using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using QuickCommerce.Domain;

namespace QuickCommerce.Infrastructure.Persistence.Configurations;

public sealed class UserStoreAssignmentConfiguration : IEntityTypeConfiguration<UserStoreAssignment>
{
    public void Configure(EntityTypeBuilder<UserStoreAssignment> builder)
    {
        builder.HasKey(assignment => new { assignment.UserId, assignment.StoreId });
        builder.Property(assignment => assignment.CreatedBy).HasMaxLength(200);
        builder.Property(assignment => assignment.UpdatedBy).HasMaxLength(200);
        builder.HasIndex(assignment => new { assignment.StoreId, assignment.IsActive, assignment.UserId });
        builder.HasOne(assignment => assignment.User)
            .WithMany(user => user.StoreAssignments)
            .HasForeignKey(assignment => assignment.UserId)
            .OnDelete(DeleteBehavior.Cascade);
        builder.HasOne(assignment => assignment.Store)
            .WithMany(store => store.UserStoreAssignments)
            .HasForeignKey(assignment => assignment.StoreId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}