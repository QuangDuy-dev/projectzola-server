using MySocialApp.Application.DTOs.Common;
using MySocialApp.Application.DTOs.Users;

namespace MySocialApp.Application.Interfaces;

public interface IUserService
{
    Task<UserDto> GetByIdAsync(Guid id);
    Task<PublicUserProfileDto> GetPublicProfileAsync(Guid userId, Guid? currentUserId = null);
    Task<PublicUserProfileDto?> GetByUsernameAsync(string username, Guid? currentUserId = null);
    Task<List<PublicUserProfileDto>> SearchUsersAsync(string query, Guid? currentUserId = null, int limit = 10);
    Task<UserDto> UpdateProfileAsync(Guid userId, UpdateUserRequest request);
    Task FollowAsync(Guid followerId, Guid followingId);
    Task UnfollowAsync(Guid followerId, Guid followingId);
    Task<PagedResult<UserDto>> GetFollowersAsync(Guid userId, PaginationParams pagination);
    Task<PagedResult<UserDto>> GetFollowingAsync(Guid userId, PaginationParams pagination);
    Task<bool> IsFollowingAsync(Guid followerId, Guid followingId);
    Task<UserDto> UpdateAvatarAsync(Guid userId, Stream fileStream, string fileName, string contentType, CancellationToken cancellationToken = default);
}
