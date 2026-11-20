using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using MySocialApp.Domain.Entities;

namespace MySocialApp.Infrastructure.Data.Configurations;

public class OrderConfiguration : IEntityTypeConfiguration<Order>
{
    public void Configure(EntityTypeBuilder<Order> builder)
    {
        builder.HasKey(o => o.Id);
        builder.Property(o => o.TotalPrice).HasColumnType("decimal(18,2)");
        builder.Property(o => o.ShippingFee).HasColumnType("decimal(18,2)");
        builder.Property(o => o.Status).HasConversion<string>().HasMaxLength(20).IsConcurrencyToken();
        builder.Property(o => o.ShipperId).IsConcurrencyToken();
        builder.Property(o => o.ShippingAddress).HasMaxLength(500).IsRequired();

        builder.HasOne(o => o.Buyer)
            .WithMany(u => u.BuyerOrders)
            .HasForeignKey(o => o.BuyerId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(o => o.Shop)
            .WithMany(s => s.Orders)
            .HasForeignKey(o => o.ShopId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(o => o.Shipper)
            .WithMany(u => u.ShipperOrders)
            .HasForeignKey(o => o.ShipperId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasIndex(o => o.BuyerId);
        builder.HasIndex(o => o.ShopId);
        builder.HasIndex(o => o.ShipperId);
        builder.HasIndex(o => o.Status);
        builder.HasIndex(o => o.CreatedAt);
        builder.HasIndex(o => o.DeliveredAt);
        builder.HasIndex(o => new { o.ShopId, o.Status });
    }
}
