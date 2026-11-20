namespace MySocialApp.Application.DTOs.Search;

public class UnifiedSearchParams
{
    private const int MaxPageSize = 50;
    private int _pageSize = 10;

    public string? Q { get; set; }
    public string Type { get; set; } = "all";
    public string Sort { get; set; } = "relevance";
    public string DateRange { get; set; } = "all";
    public int Page { get; set; } = 1;

    public int PageSize
    {
        get => _pageSize;
        set => _pageSize = value < 1 ? 10 : (value > MaxPageSize ? MaxPageSize : value);
    }
}
