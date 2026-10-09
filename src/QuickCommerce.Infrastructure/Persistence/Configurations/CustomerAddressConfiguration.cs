using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using QuickCommerce.Domain;

namespace QuickCommerce.Infrastructure.Persistence.Configurations;

public sealed class CustomerAddressConfiguration : IEntityTypeConfiguration<CustomerAddress>
{
    public void Configure(EntityTypeBuilder<CustomerAddress> builder)
    {
        builder.ToTable("CustomerAddresses");
        builder.HasKey(address => address.Id);
        builder.Property(address => address.Label).HasMaxLength(40).IsRequired();
        builder.Property(address => address.Line).HasMaxLength(300).IsRequired();
        builder.Property(address => address.FlatOrBuilding).HasMaxLength(120).IsRequired();
        builder.Property(address => address.Landmark).HasMaxLength(120).IsRequired();
        builder.Property(address => address.ReceiverName).HasMaxLength(120).IsRequired();
        builder.Property(address => address.ReceiverPhone).HasMaxLength(20).IsRequired();
        builder.Property(address => address.CreatedAt).HasColumnType("datetime2");
        builder.Property(address => address.UpdatedAt).HasColumnType("datetime2");
        builder.HasIndex(address => new { address.CustomerId, address.UpdatedAt });
        // Exactly one default address per customer, whatever two parallel requests do.
        builder.HasIndex(address => address.CustomerId).IsUnique().HasFilter("[IsDefault] = 1").HasDatabaseName("UX_CustomerAddresses_OneDefaultPerCustomer");
        builder.ToTable(table =>
        {
            table.HasCheckConstraint("CK_CustomerAddresses_Latitude", "[Latitude] BETWEEN -90 AND 90");
            table.HasCheckConstraint("CK_CustomerAddresses_Longitude", "[Longitude] BETWEEN -180 AND 180");
        });
        builder.HasOne<Customer>().WithMany().HasForeignKey(address => address.CustomerId).OnDelete(DeleteBehavior.Cascade);
    }
}
