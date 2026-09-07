using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using QuickCommerce.Domain;

namespace QuickCommerce.Infrastructure.Persistence.Configurations;

public sealed class OrganizationConfiguration : IEntityTypeConfiguration<Organization>
{
    public void Configure(EntityTypeBuilder<Organization> builder)
    {
        builder.HasKey(organization => organization.Id);
        builder.Property(organization => organization.Name).HasMaxLength(160).IsRequired();
        builder.HasIndex(organization => organization.Name).IsUnique();
        builder.HasMany(organization => organization.Stores)
            .WithOne(store => store.Organization)
            .HasForeignKey(store => store.OrganizationId)
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasMany(organization => organization.Users)
            .WithOne(user => user.Organization)
            .HasForeignKey(user => user.OrganizationId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}