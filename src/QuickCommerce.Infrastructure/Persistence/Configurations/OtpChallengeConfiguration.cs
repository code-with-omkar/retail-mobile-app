using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using QuickCommerce.Domain;

namespace QuickCommerce.Infrastructure.Persistence.Configurations;

public sealed class OtpChallengeConfiguration : IEntityTypeConfiguration<OtpChallenge>
{
    public void Configure(EntityTypeBuilder<OtpChallenge> builder)
    {
        builder.ToTable("OtpChallenges");
        builder.HasKey(item => item.Id);
        builder.Property(item => item.Phone).HasMaxLength(20).IsRequired();
        builder.Property(item => item.Purpose).HasMaxLength(16).IsRequired();
        builder.Property(item => item.CodeHash).HasMaxLength(64).IsRequired();
        builder.Property(item => item.CreatedAt).HasColumnType("datetime2");
        builder.Property(item => item.ExpiresAt).HasColumnType("datetime2");
        builder.Property(item => item.UsedAt).HasColumnType("datetime2");
        builder.Property(item => item.RequestedFromIp).HasMaxLength(64);
        builder.HasIndex(item => new { item.Phone, item.CreatedAt });
        builder.HasIndex(item => item.ExpiresAt);
    }
}
