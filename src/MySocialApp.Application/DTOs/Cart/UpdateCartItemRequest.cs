using System.ComponentModel.DataAnnotations;

namespace MySocialApp.Application.DTOs.Cart;

public class UpdateCartItemRequest
{
    [Required]
    [Range(1, int.MaxValue)]
    public int Quantity { get; set; }
}
