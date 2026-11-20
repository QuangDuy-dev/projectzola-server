namespace MySocialApp.Application.DTOs.Search;

public class ProductSearchParams
{
    public string? Q { get; set; }
    public Guid? CategoryId { get; set; }
    public Guid? ShopId { get; set; }
    public decimal? MinPrice { get; set; }
    public decimal? MaxPrice { get; set; }
    public string? Sort { get; set; } = "newest";
    public int Page { get; set; } = 1;
    public int PageSize { get; set; } = 20;
}
