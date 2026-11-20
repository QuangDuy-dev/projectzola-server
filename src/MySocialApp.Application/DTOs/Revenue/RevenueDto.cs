namespace MySocialApp.Application.DTOs.Revenue;

public class RevenueDto
{
    public decimal TotalRevenue { get; set; }
    public decimal GrossMerchandiseValue => TotalRevenue;
    public int TotalOrders { get; set; }
    public int DeliveredOrderCount => TotalOrders;
    public int TotalItemsSold { get; set; }
    public decimal TotalShippingFees { get; set; }
    public DateTime From { get; set; }
    public DateTime To { get; set; }
}
