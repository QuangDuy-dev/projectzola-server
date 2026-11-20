using System.ComponentModel.DataAnnotations;

namespace MySocialApp.Application.DTOs.Products;

public class UpdateProductRequest
{
    [StringLength(300, MinimumLength = 1)]
    public string? Name { get; set; }

    [StringLength(5000)]
    public string? Description { get; set; }

    [Range(0, double.MaxValue)]
    public decimal? Price { get; set; }

    [Range(0, int.MaxValue)]
    public int? Stock { get; set; }

    [StringLength(100)]
    public string? Sku { get; set; }

    public Guid? CategoryId { get; set; }

    public bool? IsActive { get; set; }
}
