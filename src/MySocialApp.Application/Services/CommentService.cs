using Microsoft.EntityFrameworkCore;
using MySocialApp.Application.DTOs.Comments;
using MySocialApp.Application.DTOs.Common;
using MySocialApp.Application.Interfaces;
using MySocialApp.Domain.Entities;
using MySocialApp.Domain.Enums;

namespace MySocialApp.Application.Services;

public class CommentService : ICommentService
{
    private readonly IApplicationDbContext _context;
    private readonly INotificationService? _notificationService;

    public CommentService(
        IApplicationDbContext context,
        INotificationService? notificationService = null)
    {
        _context = context;
        _notificationService = notificationService;
    }

    public async Task<PagedResult<CommentDto>> GetByPostAsync(Guid postId, PaginationParams pagination)
    {
        var query = _context.Comments
            .Include(c => c.User)
            .Include(c => c.Replies)
            .Where(c => c.PostId == postId && c.ParentCommentId == null)
            .OrderByDescending(c => c.CreatedAt);

        var totalItems = await query.CountAsync();
        var comments = await query
            .Skip((pagination.Page - 1) * pagination.PageSize)
            .Take(pagination.PageSize)
            .ToListAsync();

        return new PagedResult<CommentDto>
        {
            Items = comments.Select(MapToDto).ToList(),
            Page = pagination.Page,
            PageSize = pagination.PageSize,
            TotalItems = totalItems
        };
    }

    public async Task<CommentDto> CreateAsync(Guid postId, Guid userId, CreateCommentRequest request)
    {
        var post = await _context.Posts.FindAsync(postId)
            ?? throw new KeyNotFoundException("Post not found.");

        if (request.ParentCommentId.HasValue)
        {
            var parent = await _context.Comments.FindAsync(request.ParentCommentId.Value)
                ?? throw new KeyNotFoundException("Parent comment not found.");
            if (parent.PostId != postId)
                throw new InvalidOperationException("Parent comment belongs to a different post.");
        }

        var comment = new Comment
        {
            PostId = postId,
            UserId = userId,
            ParentCommentId = request.ParentCommentId,
            Content = request.Content
        };

        _context.Comments.Add(comment);
        await _context.SaveChangesAsync();

        var saved = await _context.Comments
            .Include(c => c.User)
            .Include(c => c.Replies)
            .FirstAsync(c => c.Id == comment.Id);

        if (_notificationService != null && post.UserId != userId)
        {
            var commenter = await _context.Users.FindAsync(userId);
            var commenterName = commenter?.DisplayName ?? commenter?.Username ?? "Someone";
            await _notificationService.CreateAsync(
                post.UserId,
                NotificationType.PostCommented,
                "New Comment",
                $"{commenterName} commented on your post.",
                postId);
        }

        return MapToDto(saved);
    }

    public async Task<CommentDto> UpdateAsync(Guid commentId, Guid userId, UpdateCommentRequest request)
    {
        var comment = await _context.Comments
            .Include(c => c.User)
            .Include(c => c.Replies)
            .FirstOrDefaultAsync(c => c.Id == commentId)
            ?? throw new KeyNotFoundException("Comment not found.");

        if (comment.UserId != userId)
            throw new UnauthorizedAccessException("Not authorized to update this comment.");

        comment.Content = request.Content;
        await _context.SaveChangesAsync();

        return MapToDto(comment);
    }

    public async Task DeleteAsync(Guid commentId, Guid userId, bool isAdmin = false)
    {
        var comment = await _context.Comments.FindAsync(commentId)
            ?? throw new KeyNotFoundException("Comment not found.");

        if (!isAdmin && comment.UserId != userId)
            throw new UnauthorizedAccessException("Not authorized to delete this comment.");

        comment.IsDeleted = true;
        await _context.SaveChangesAsync();
    }

    private static CommentDto MapToDto(Comment comment)
    {
        return new CommentDto
        {
            Id = comment.Id,
            PostId = comment.PostId,
            UserId = comment.UserId,
            Username = comment.User.Username,
            DisplayName = comment.User.DisplayName,
            UserAvatarUrl = comment.User.AvatarUrl,
            ParentCommentId = comment.ParentCommentId,
            Content = comment.Content,
            ReplyCount = comment.Replies.Count,
            CreatedAt = comment.CreatedAt,
            UpdatedAt = comment.UpdatedAt
        };
    }
}
