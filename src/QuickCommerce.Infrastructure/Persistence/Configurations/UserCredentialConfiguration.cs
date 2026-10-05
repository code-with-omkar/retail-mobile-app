using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using QuickCommerce.Domain;

namespace QuickCommerce.Infrastructure.Persistence.Configurations;

public sealed class UserCredentialConfiguration : IEntityTypeConfiguration<UserCredential>
{
    public void Configure(EntityTypeBuilder<UserCredential> builder)
    {
        builder.HasKey(credential => credential.Id);
        builder.Property(credential => credential.PasswordHash).HasMaxLength(500).IsRequired();
        builder.Property(credential => credential.HashVersion).HasMaxLength(80).IsRequired();
        builder.Property(credential => credential.CreatedBy).HasMaxLength(200);
        builder.Property(credential => credential.UpdatedBy).HasMaxLength(200);
        builder.HasIndex(credential => credential.UserId).IsUnique();
        builder.HasOne(credential => credential.User)
            .WithOne(user => user.Credential)
            .HasForeignKey<UserCredential>(credential => credential.UserId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}