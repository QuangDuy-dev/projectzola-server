using System.ComponentModel.DataAnnotations;

namespace MySocialApp.Application.DTOs.Categories;

public class CreateCategoryRequest
{
    [Required]
    [StringLength(100, MinimumLength = 1)]
    public string Name { get; set; } = string.Empty;

    [StringLength(500)]
    public string? Description { get; set; }

    public Guid? ParentCategoryId { get; set; }
}
