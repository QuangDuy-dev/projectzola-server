using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using MySocialApp.Domain.Entities;

namespace MySocialApp.Infrastructure.Data.Configurations;

public class ShipperProfileConfiguration : IEntityTypeConfiguration<ShipperProfile>
{
    public void Configure(EntityTypeBuilder<ShipperProfile> builder)
    {
        builder.HasKey(sp => sp.Id);
        builder.Property(sp => sp.Phone).HasMaxLength(20);
        builder.Property(sp => sp.VehicleType).HasMaxLength(50);
        builder.Property(sp => sp.VehiclePlate).HasMaxLength(20);

        builder.HasOne(sp => sp.User)
            .WithOne(u => u.ShipperProfile)
            .HasForeignKey<ShipperProfile>(sp => sp.UserId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasIndex(sp => sp.UserId).IsUnique();
    }
}
