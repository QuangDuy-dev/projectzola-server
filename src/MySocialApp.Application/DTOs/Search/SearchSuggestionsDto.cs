using MySocialApp.Application.DTOs.Posts;

namespace MySocialApp.Application.DTOs.Search;

public class SearchSuggestionsDto
{
    public List<UserSearchDto> Users { get; set; } = new();
    public List<ProductSearchDto> Products { get; set; } = new();
    public List<ShopSearchDto> Shops { get; set; } = new();
    public List<PostDto> Posts { get; set; } = new();
}
