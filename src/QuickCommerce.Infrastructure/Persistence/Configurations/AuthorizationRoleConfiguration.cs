using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using QuickCommerce.Domain;

namespace QuickCommerce.Infrastructure.Persistence.Configurations;

public sealed class AuthorizationRoleConfiguration : IEntityTypeConfiguration<AuthorizationRole>
{
    public void Configure(EntityTypeBuilder<AuthorizationRole> builder)
    {
        builder.HasKey(role => role.Id);
        builder.Property(role => role.Name).HasMaxLength(160).IsRequired();
        builder.Property(role => role.Code).HasMaxLength(80).IsRequired();
        builder.Property(role => role.CreatedBy).HasMaxLength(200);
        builder.Property(role => role.UpdatedBy).HasMaxLength(200);
        builder.HasIndex(role => role.Code).IsUnique();
        builder.HasMany(role => role.UserRoles)
            .WithOne(userRole => userRole.Role)
            .HasForeignKey(userRole => userRole.RoleId)
            .OnDelete(DeleteBehavior.Cascade);
        builder.HasMany(role => role.RolePermissions)
            .WithOne(rolePermission => rolePermission.Role)
            .HasForeignKey(rolePermission => rolePermission.RoleId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}