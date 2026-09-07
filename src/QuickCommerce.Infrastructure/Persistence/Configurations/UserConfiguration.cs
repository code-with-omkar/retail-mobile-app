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
        builder.Property(user => user.Role).HasConversion<string>().HasMaxLength(32).IsRequired();
        builder.HasIndex(user => user.ExternalSubject).IsUnique();
        builder.HasIndex(user => new { user.OrganizationId, user.StoreId });
        builder.HasOne(user => user.Organization)
            .WithMany(organization => organization.Users)
            .HasForeignKey(user => user.OrganizationId)
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(user => user.Store)
            .WithMany(store => store.Users)
            .HasForeignKey(user => user.StoreId)
            .OnDelete(DeleteBehavior.SetNull);
    }
}