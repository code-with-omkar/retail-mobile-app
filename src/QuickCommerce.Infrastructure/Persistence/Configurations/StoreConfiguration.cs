using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using QuickCommerce.Domain;

namespace QuickCommerce.Infrastructure.Persistence.Configurations;

public sealed class StoreConfiguration : IEntityTypeConfiguration<Store>
{
    public void Configure(EntityTypeBuilder<Store> builder)
    {
        builder.HasKey(store => store.Id);
        builder.HasOne(store => store.Organization)
            .WithMany(organization => organization.Stores)
            .HasForeignKey(store => store.OrganizationId)
            .OnDelete(DeleteBehavior.Restrict);
        builder.Property(store => store.Name).HasMaxLength(160).IsRequired();
        builder.Property(store => store.Address).HasMaxLength(500).IsRequired();
        builder.Property(store => store.Latitude).HasPrecision(9, 6);
        builder.Property(store => store.Longitude).HasPrecision(9, 6);
        builder.Property(store => store.ServiceRadiusKm).HasPrecision(9, 2);
        builder.Property(store => store.PhoneNumber).HasMaxLength(20);
        builder.Property(store => store.Code).HasMaxLength(8);
        builder.HasIndex(store => store.Code).IsUnique().HasFilter("[Code] IS NOT NULL");
        builder.HasIndex(store => store.IsActive);
        builder.HasIndex(store => new { store.OrganizationId, store.Name }).IsUnique();
    }
}
