using System.ComponentModel.DataAnnotations;

namespace MySocialApp.Application.DTOs.Orders;

public class CreateOrderRequest
{
    [Required]
    [StringLength(500, MinimumLength = 1)]
    public string ShippingAddress { get; set; } = string.Empty;
}
