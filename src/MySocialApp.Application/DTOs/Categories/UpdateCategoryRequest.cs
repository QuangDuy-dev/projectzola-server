using System.ComponentModel.DataAnnotations;

namespace MySocialApp.Application.DTOs.Categories;

public class UpdateCategoryRequest
{
    [StringLength(100, MinimumLength = 1)]
    public string? Name { get; set; }

    [StringLength(500)]
    public string? Description { get; set; }

    public Guid? ParentCategoryId { get; set; }

    public bool? IsActive { get; set; }
}
