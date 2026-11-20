using MySocialApp.Application.DTOs.Common;
using MySocialApp.Application.DTOs.Posts;

namespace MySocialApp.Application.DTOs.Search;

public class UnifiedSearchResponse
{
    public PagedResult<UserSearchDto> Users { get; set; } = new();
    public PagedResult<PostDto> Posts { get; set; } = new();
    public PagedResult<PostDto> Shorts { get; set; } = new();
    public PagedResult<ProductSearchDto> Products { get; set; } = new();
    public PagedResult<ShopSearchDto> Shops { get; set; } = new();
}
