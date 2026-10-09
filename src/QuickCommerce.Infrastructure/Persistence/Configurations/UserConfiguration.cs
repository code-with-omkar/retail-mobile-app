using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using QuickCommerce.Domain;

namespace QuickCommerce.Infrastructure.Persistence.Configurations;

public sealed class UserConfiguration : IEntityTypeConfiguration<User>
{
    public void Configure(EntityTypeBuilder<User> builder)
    {
        builder.HasKey(user => user.Id);
        builder.Property(user => user.ExternalSubject).HasMaxLength(200).IsRequired();
        builder.Property(user => user.DisplayName).HasMaxLength(160).IsRequired();
        builder.Property(user => user.FirstName).HasMaxLength(80);
        builder.Property(user => user.LastName).HasMaxLength(80);
        builder.Property(user => user.Email).HasMaxLength(160);
        builder.Property(user => user.PhoneNumber).HasMaxLength(20);
        builder.Property(user => user.CreatedBy).HasMaxLength(200);
        builder.Property(user => user.UpdatedBy).HasMaxLength(200);
        builder.Property(user => user.Role).HasConversion<string>().HasMaxLength(32).IsRequired();
        builder.Property(user => user.StaffCategory).HasConversion<string>().HasMaxLength(32);
        builder.HasIndex(user => user.ExternalSubject).IsUnique();
        builder.HasIndex(user => user.Email).IsUnique(false);
        builder.HasIndex(user => new { user.OrganizationId, user.StoreId });
        builder.HasOne(user => user.Organization)
            .WithMany(organization => organization.Users)
            .HasForeignKey(user => user.OrganizationId)
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(user => user.Store)
            .WithMany(store => store.Users)
            .HasForeignKey(user => user.StoreId)
            .OnDelete(DeleteBehavior.SetNull);
        builder.HasMany(user => user.UserRoles)
            .WithOne(userRole => userRole.User)
            .HasForeignKey(userRole => userRole.UserId)
            .OnDelete(DeleteBehavior.Cascade);
        builder.HasMany(user => user.StoreAssignments)
            .WithOne(assignment => assignment.User)
            .HasForeignKey(assignment => assignment.UserId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}