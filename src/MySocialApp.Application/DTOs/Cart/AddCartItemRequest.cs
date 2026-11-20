using System.ComponentModel.DataAnnotations;

namespace MySocialApp.Application.DTOs.Cart;

public class AddCartItemRequest
{
    [Required]
    public Guid ProductId { get; set; }

    [Required]
    [Range(1, int.MaxValue)]
    public int Quantity { get; set; } = 1;
}
