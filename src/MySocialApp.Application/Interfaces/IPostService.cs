using MySocialApp.Application.DTOs.Common;
using MySocialApp.Application.DTOs.Posts;

namespace MySocialApp.Application.Interfaces;

public interface IPostService
{
    Task<PagedResult<PostDto>> GetPostsAsync(PaginationParams pagination, Guid? currentUserId = null);
    Task<PostDto> GetByIdAsync(Guid id, Guid? currentUserId = null);
    Task<PostDto> CreateAsync(Guid userId, CreatePostRequest request);
    Task<PostDto> UpdateAsync(Guid postId, Guid userId, UpdatePostRequest request);
    Task DeleteAsync(Guid postId, Guid userId);
    Task<PagedResult<PostDto>> GetUserPostsAsync(Guid userId, PaginationParams pagination, Guid? currentUserId = null);
    Task<string?> ToggleReactionAsync(Guid postId, Guid userId, string reactionType);
    Task RemoveReactionAsync(Guid postId, Guid userId);
    Task<PostMediaDto> AddMediaAsync(Guid postId, Guid userId, Stream fileStream, string fileName, string contentType, CancellationToken cancellationToken = default);
    Task DeleteMediaAsync(Guid postId, Guid mediaId, Guid userId, CancellationToken cancellationToken = default);
}
