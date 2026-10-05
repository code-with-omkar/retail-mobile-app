using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using QuickCommerce.Domain;

namespace QuickCommerce.Infrastructure.Persistence.Configurations;

public sealed class AuthorizationPermissionConfiguration : IEntityTypeConfiguration<AuthorizationPermission>
{
    public void Configure(EntityTypeBuilder<AuthorizationPermission> builder)
    {
        builder.HasKey(permission => permission.Id);
        builder.Property(permission => permission.Name).HasMaxLength(160).IsRequired();
        builder.Property(permission => permission.Code).HasMaxLength(120).IsRequired();
        builder.Property(permission => permission.CreatedBy).HasMaxLength(200);
        builder.Property(permission => permission.UpdatedBy).HasMaxLength(200);
        builder.HasIndex(permission => permission.Code).IsUnique();
        builder.HasMany(permission => permission.RolePermissions)
            .WithOne(rolePermission => rolePermission.Permission)
            .HasForeignKey(rolePermission => rolePermission.PermissionId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}