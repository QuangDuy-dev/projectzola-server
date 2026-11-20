namespace MySocialApp.Application.DTOs.Search;

public class ProductSearchDto
{
    public Guid Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }
    public decimal Price { get; set; }
    public int Stock { get; set; }
    public string? ThumbnailUrl { get; set; }
    public ProductSearchShopDto Shop { get; set; } = null!;
    public ProductSearchCategoryDto? Category { get; set; }
    public DateTime CreatedAt { get; set; }
}

public class ProductSearchShopDto
{
    public Guid Id { get; set; }
    public string Name { get; set; } = string.Empty;
}

public class ProductSearchCategoryDto
{
    public Guid Id { get; set; }
    public string Name { get; set; } = string.Empty;
}
