using System.Security.Claims;
using Microsoft.AspNetCore.Mvc;
using MySocialApp.Application.DTOs.Common;
using MySocialApp.Application.DTOs.Search;
using MySocialApp.Application.Interfaces;

namespace MySocialApp.Api.Controllers;

[ApiController]
[Route("api/search")]
public class SearchController : ControllerBase
{
    private readonly ISearchService _searchService;

    public SearchController(ISearchService searchService)
    {
        _searchService = searchService;
    }

    [HttpGet]
    public async Task<ActionResult<ApiResponse<UnifiedSearchResponse>>> UnifiedSearch([FromQuery] UnifiedSearchParams searchParams)
    {
        var currentUserId = GetCurrentUserId();
        var result = await _searchService.UnifiedSearchAsync(searchParams, currentUserId);
        return Ok(ApiResponse<UnifiedSearchResponse>.SuccessResponse(result));
    }

    [HttpGet("suggestions")]
    public async Task<ActionResult<ApiResponse<SearchSuggestionsDto>>> GetSuggestions([FromQuery] string? q)
    {
        var currentUserId = GetCurrentUserId();
        var result = await _searchService.GetSuggestionsAsync(q, currentUserId);
        return Ok(ApiResponse<SearchSuggestionsDto>.SuccessResponse(result));
    }

    [HttpGet("products")]
    public async Task<ActionResult<ApiResponse<PagedResult<ProductSearchDto>>>> SearchProducts([FromQuery] ProductSearchParams searchParams)
    {
        var result = await _searchService.SearchProductsAsync(searchParams);
        return Ok(ApiResponse<PagedResult<ProductSearchDto>>.SuccessResponse(result));
    }

    [HttpGet("shops")]
    public async Task<ActionResult<ApiResponse<PagedResult<ShopSearchDto>>>> SearchShops(
        [FromQuery] string? q,
        [FromQuery] PaginationParams pagination)
    {
        var result = await _searchService.SearchShopsAsync(q, pagination);
        return Ok(ApiResponse<PagedResult<ShopSearchDto>>.SuccessResponse(result));
    }

    private Guid? GetCurrentUserId()
    {
        var claim = User.FindFirstValue(ClaimTypes.NameIdentifier);
        return Guid.TryParse(claim, out var id) ? id : null;
    }
}
