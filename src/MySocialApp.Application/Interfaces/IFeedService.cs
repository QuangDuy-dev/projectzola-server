using MySocialApp.Application.DTOs.Common;
using MySocialApp.Application.DTOs.Feed;

namespace MySocialApp.Application.Interfaces;

public interface IFeedService
{
    Task<CursorPagedResult<FeedPostDto>> GetSocialFeedAsync(Guid currentUserId, int limit = 20, string? cursor = null);
    Task<CursorPagedResult<FeedPostDto>> GetVideoFeedAsync(Guid currentUserId, int limit = 10, string? cursor = null);
}
