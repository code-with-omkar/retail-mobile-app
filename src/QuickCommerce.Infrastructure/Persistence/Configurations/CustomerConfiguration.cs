using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using QuickCommerce.Domain;

namespace QuickCommerce.Infrastructure.Persistence.Configurations;

public sealed class CustomerConfiguration : IEntityTypeConfiguration<Customer>
{
    public void Configure(EntityTypeBuilder<Customer> builder)
    {
        builder.HasKey(customer => customer.Id);
        builder.HasIndex(customer => customer.UserId).IsUnique();
        builder.Property(customer => customer.OffersEnabled).HasDefaultValue(true);
        builder.HasOne(customer => customer.User)
            .WithOne(user => user.Customer)
            .HasForeignKey<Customer>(customer => customer.UserId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}