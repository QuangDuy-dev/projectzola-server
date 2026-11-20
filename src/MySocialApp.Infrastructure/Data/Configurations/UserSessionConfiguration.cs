using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using MySocialApp.Domain.Entities;

namespace MySocialApp.Infrastructure.Data.Configurations;

public class UserSessionConfiguration : IEntityTypeConfiguration<UserSession>
{
    public void Configure(EntityTypeBuilder<UserSession> builder)
    {
        builder.HasKey(s => s.Id);
        builder.Property(s => s.RefreshTokenHash).HasMaxLength(128).IsRequired();
        builder.Property(s => s.DeviceInfo).HasMaxLength(256).IsRequired();
        builder.Property(s => s.IpAddress).HasMaxLength(64);
        builder.Property(s => s.ExpiresAt).IsRequired();
        builder.Property(s => s.LastUsedAt).IsRequired();

        builder.Ignore(s => s.IsRevoked);

        builder.HasIndex(s => s.RefreshTokenHash).IsUnique();
        builder.HasIndex(s => new { s.UserId, s.RevokedAt, s.ExpiresAt });

        builder.HasOne(s => s.User)
            .WithMany(u => u.Sessions)
            .HasForeignKey(s => s.UserId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
