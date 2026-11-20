using Microsoft.EntityFrameworkCore;
using MySocialApp.Application.Common;
using MySocialApp.Application.DTOs.Common;
using MySocialApp.Application.DTOs.Feed;
using MySocialApp.Application.Interfaces;
using MySocialApp.Domain.Entities;
using MySocialApp.Domain.Enums;

namespace MySocialApp.Application.Services;

public class FeedService : IFeedService
{
    private readonly IApplicationDbContext _context;

    public FeedService(IApplicationDbContext context)
    {
        _context = context;
    }

    public async Task<CursorPagedResult<FeedPostDto>> GetSocialFeedAsync(Guid currentUserId, int limit = 20, string? cursor = null)
    {
        if (limit < 1 || limit > 50)
            throw new ArgumentOutOfRangeException(nameof(limit), "Limit must be between 1 and 50.");

        var decodedCursor = FeedCursorCodec.Decode(cursor);

        // Fetch IDs of users the current user follows
        var followedUserIds = await _context.Follows
            .Where(f => f.FollowerId == currentUserId)
            .Select(f => f.FollowingId)
            .ToListAsync();

        var feedAuthorIds = followedUserIds.Append(currentUserId).Distinct().ToList();

        // If cursor was already in fallback mode, directly query public fallback posts
        if (decodedCursor != null && decodedCursor.IsFallback)
        {
            var fallbackQuery = _context.Posts
                .AsNoTracking()
                .Where(p => !feedAuthorIds.Contains(p.UserId) && p.User.IsActive);

            fallbackQuery = fallbackQuery.Where(p => p.CreatedAt < decodedCursor.CreatedAt
                || (p.CreatedAt == decodedCursor.CreatedAt && p.Id < decodedCursor.PostId));

            var fallbackItems = await ProjectToDto(
                fallbackQuery.OrderByDescending(p => p.CreatedAt).ThenByDescending(p => p.Id),
                currentUserId,
                followedUserIds)
                .Take(limit + 1)
                .ToListAsync();

            bool fallbackHasMore = fallbackItems.Count > limit;
            var finalFallbackItems = fallbackHasMore ? fallbackItems.Take(limit).ToList() : fallbackItems;

            string? nextFallbackCursor = null;
            if (fallbackHasMore && finalFallbackItems.Count > 0)
            {
                var lastItem = finalFallbackItems.Last();
                nextFallbackCursor = FeedCursorCodec.Encode(lastItem.CreatedAt, lastItem.Id, isFallback: true);
            }

            return new CursorPagedResult<FeedPostDto>
            {
                Items = finalFallbackItems,
                NextCursor = nextFallbackCursor,
                HasMore = fallbackHasMore
            };
        }

        // Query 1: Posts from followed users and current user
        var followQuery = _context.Posts
            .AsNoTracking()
            .Where(p => feedAuthorIds.Contains(p.UserId) && p.User.IsActive);

        if (decodedCursor != null)
        {
            followQuery = followQuery.Where(p => p.CreatedAt < decodedCursor.CreatedAt
                || (p.CreatedAt == decodedCursor.CreatedAt && p.Id < decodedCursor.PostId));
        }

        var orderedFollowQuery = followQuery
            .OrderByDescending(p => p.CreatedAt)
            .ThenByDescending(p => p.Id);

        var rawItems = await ProjectToDto(orderedFollowQuery, currentUserId, followedUserIds)
            .Take(limit + 1)
            .ToListAsync();

        var fallbackItemIds = new HashSet<Guid>();

        // Fallback strategy: If followed posts are insufficient to fill (limit + 1),
        // supplement with recent public posts from other active users without duplication.
        if (rawItems.Count < limit + 1)
        {
            var existingPostIds = rawItems.Select(x => x.Id).ToList();
            var needed = (limit + 1) - rawItems.Count;

            var fallbackQuery = _context.Posts
                .AsNoTracking()
                .Where(p => !feedAuthorIds.Contains(p.UserId) && p.User.IsActive && !existingPostIds.Contains(p.Id));

            var fallbackItems = await ProjectToDto(
                fallbackQuery.OrderByDescending(p => p.CreatedAt).ThenByDescending(p => p.Id),
                currentUserId,
                followedUserIds)
                .Take(needed)
                .ToListAsync();

            foreach (var item in fallbackItems)
            {
                fallbackItemIds.Add(item.Id);
            }

            rawItems.AddRange(fallbackItems);
        }

        bool hasMore = rawItems.Count > limit;
        var finalItems = hasMore ? rawItems.Take(limit).ToList() : rawItems;

        string? nextCursor = null;
        if (hasMore && finalItems.Count > 0)
        {
            var lastItem = finalItems.Last();
            bool isLastItemFallback = fallbackItemIds.Contains(lastItem.Id);
            nextCursor = FeedCursorCodec.Encode(lastItem.CreatedAt, lastItem.Id, isFallback: isLastItemFallback);
        }

        return new CursorPagedResult<FeedPostDto>
        {
            Items = finalItems,
            NextCursor = nextCursor,
            HasMore = hasMore
        };
    }

    public async Task<CursorPagedResult<FeedPostDto>> GetVideoFeedAsync(Guid currentUserId, int limit = 10, string? cursor = null)
    {
        if (limit < 1 || limit > 50)
            throw new ArgumentOutOfRangeException(nameof(limit), "Limit must be between 1 and 50.");

        var decodedCursor = FeedCursorCodec.Decode(cursor);

        var followedUserIds = await _context.Follows
            .Where(f => f.FollowerId == currentUserId)
            .Select(f => f.FollowingId)
            .ToListAsync();

        var videoQuery = _context.Posts
            .AsNoTracking()
            .Where(p => p.User.IsActive && p.Media.Any(m => m.MediaType == MediaType.Video));

        if (decodedCursor != null)
        {
            videoQuery = videoQuery.Where(p => p.CreatedAt < decodedCursor.CreatedAt
                || (p.CreatedAt == decodedCursor.CreatedAt && p.Id < decodedCursor.PostId));
        }

        var orderedVideoQuery = videoQuery
            .OrderByDescending(p => p.CreatedAt)
            .ThenByDescending(p => p.Id);

        var rawItems = await ProjectToDto(orderedVideoQuery, currentUserId, followedUserIds)
            .Take(limit + 1)
            .ToListAsync();

        bool hasMore = rawItems.Count > limit;
        var finalItems = hasMore ? rawItems.Take(limit).ToList() : rawItems;

        string? nextCursor = null;
        if (hasMore && finalItems.Count > 0)
        {
            var lastItem = finalItems.Last();
            nextCursor = FeedCursorCodec.Encode(lastItem.CreatedAt, lastItem.Id);
        }

        return new CursorPagedResult<FeedPostDto>
        {
            Items = finalItems,
            NextCursor = nextCursor,
            HasMore = hasMore
        };
    }

    private static IQueryable<FeedPostDto> ProjectToDto(
        IQueryable<Post> query,
        Guid currentUserId,
        IReadOnlyCollection<Guid> followedUserIds)
    {
        return query.Select(p => new FeedPostDto
        {
            Id = p.Id,
            Author = new FeedAuthorDto
            {
                Id = p.User.Id,
                Username = p.User.Username,
                DisplayName = p.User.DisplayName,
                AvatarUrl = p.User.AvatarUrl
            },
            Content = p.Content,
            Media = p.Media
                .OrderBy(m => m.CreatedAt)
                .Select(m => new FeedMediaDto
                {
                    Id = m.Id,
                    MediaType = m.MediaType.ToString(),
                    Url = m.StorageUrl,
                    ThumbnailUrl = m.ThumbnailUrl
                }).ToList(),
            LikeCount = p.Reactions.Count(r => r.ReactionType == ReactionType.Like),
            DislikeCount = p.Reactions.Count(r => r.ReactionType == ReactionType.Dislike),
            CommentCount = p.Comments.Count(c => !c.IsDeleted),
            CurrentUserReaction = p.Reactions
                .Where(r => r.UserId == currentUserId)
                .Select(r => r.ReactionType.ToString())
                .FirstOrDefault(),
            IsFollowingAuthor = p.UserId != currentUserId && followedUserIds.Contains(p.UserId),
            CreatedAt = p.CreatedAt,
            EditedAt = p.UpdatedAt != p.CreatedAt ? p.UpdatedAt : null
        });
    }
}
