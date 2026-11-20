using MySocialApp.Application.DTOs.Comments;
using MySocialApp.Application.DTOs.Common;

namespace MySocialApp.Application.Interfaces;

public interface ICommentService
{
    Task<PagedResult<CommentDto>> GetByPostAsync(Guid postId, PaginationParams pagination);
    Task<CommentDto> CreateAsync(Guid postId, Guid userId, CreateCommentRequest request);
    Task<CommentDto> UpdateAsync(Guid commentId, Guid userId, UpdateCommentRequest request);
    Task DeleteAsync(Guid commentId, Guid userId, bool isAdmin = false);
}
