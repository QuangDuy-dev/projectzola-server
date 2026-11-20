using MySocialApp.Domain.Common;
using MySocialApp.Domain.Enums;

namespace MySocialApp.Domain.Entities;

public class Order : BaseEntity
{
    public Guid BuyerId { get; set; }
    public User Buyer { get; set; } = null!;
    public Guid ShopId { get; set; }
    public Shop Shop { get; set; } = null!;
    public Guid? ShipperId { get; set; }
    public User? Shipper { get; set; }
    public decimal TotalPrice { get; set; }
    public decimal ShippingFee { get; set; }
    public OrderStatus Status { get; set; } = OrderStatus.Pending;
    public string ShippingAddress { get; set; } = string.Empty;
    public DateTime? DeliveredAt { get; set; }
    public DateTime? CancelledAt { get; set; }

    public ICollection<OrderItem> Items { get; set; } = new List<OrderItem>();
}
