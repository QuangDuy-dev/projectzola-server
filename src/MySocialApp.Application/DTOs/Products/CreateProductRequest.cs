using System.ComponentModel.DataAnnotations;

namespace MySocialApp.Application.DTOs.Products;

public class CreateProductRequest
{
    [Required]
    [StringLength(300, MinimumLength = 1)]
    public string Name { get; set; } = string.Empty;

    [StringLength(5000)]
    public string? Description { get; set; }

    [Required]
    [Range(0, double.MaxValue)]
    public decimal Price { get; set; }

    [Required]
    [Range(0, int.MaxValue)]
    public int Stock { get; set; }

    [StringLength(100)]
    public string? Sku { get; set; }

    public Guid? CategoryId { get; set; }
}
