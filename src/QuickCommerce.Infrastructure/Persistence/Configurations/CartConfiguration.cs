using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using QuickCommerce.Domain;

namespace QuickCommerce.Infrastructure.Persistence.Configurations;

public sealed class CartConfiguration : IEntityTypeConfiguration<Cart>
{
    public void Configure(EntityTypeBuilder<Cart> builder)
    {
        builder.HasKey(cart => cart.Id);
        builder.HasIndex(cart => new { cart.CustomerId, cart.StoreId }).IsUnique();
        builder.Property(cart => cart.CreatedAt).HasColumnType("datetime2").IsRequired();
        builder.Property(cart => cart.UpdatedAt).HasColumnType("datetime2").IsRequired();
        builder.HasOne(cart => cart.Customer)
            .WithMany(customer => customer.Carts)
            .HasForeignKey(cart => cart.CustomerId)
            .OnDelete(DeleteBehavior.Cascade);
        builder.HasOne(cart => cart.Store)
            .WithMany(store => store.Carts)
            .HasForeignKey(cart => cart.StoreId)
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasMany(cart => cart.Items)
            .WithOne(item => item.Cart)
            .HasForeignKey(item => item.CartId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}