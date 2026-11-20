using MySocialApp.Application.DTOs.Common;
using MySocialApp.Application.DTOs.Search;

namespace MySocialApp.Application.Interfaces;

public interface ISearchService
{
    Task<PagedResult<ProductSearchDto>> SearchProductsAsync(ProductSearchParams searchParams);
    Task<PagedResult<ShopSearchDto>> SearchShopsAsync(string? q, PaginationParams pagination);
    Task<UnifiedSearchResponse> UnifiedSearchAsync(UnifiedSearchParams searchParams, Guid? currentUserId = null);
    Task<SearchSuggestionsDto> GetSuggestionsAsync(string? q, Guid? currentUserId = null);
}
