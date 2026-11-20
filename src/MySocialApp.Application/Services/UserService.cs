using Microsoft.EntityFrameworkCore;
using MySocialApp.Application.DTOs.Common;
using MySocialApp.Application.DTOs.Users;
using MySocialApp.Application.Interfaces;
using MySocialApp.Domain.Entities;
using MySocialApp.Domain.Enums;

namespace MySocialApp.Application.Services;

public class UserService : IUserService
{
    private readonly IApplicationDbContext _context;
    private readonly IMediaStorageService? _mediaStorageService;
    private readonly IFileValidator? _fileValidator;
    private readonly IImageProcessingService? _imageProcessingService;
    private readonly INotificationService? _notificationService;

    public UserService(
        IApplicationDbContext context,
        IMediaStorageService? mediaStorageService = null,
        IFileValidator? fileValidator = null,
        IImageProcessingService? imageProcessingService = null,
        INotificationService? notificationService = null)
    {
        _context = context;
        _mediaStorageService = mediaStorageService;
        _fileValidator = fileValidator;
        _imageProcessingService = imageProcessingService;
        _notificationService = notificationService;
    }

    public async Task<UserDto> GetByIdAsync(Guid id)
    {
        var user = await _context.Users.FindAsync(id)
            ?? throw new KeyNotFoundException("User not found.");

        return await MapToDto(user);
    }

    public async Task<PublicUserProfileDto> GetPublicProfileAsync(Guid userId, Guid? currentUserId = null)
    {
        var user = await _context.Users.FindAsync(userId)
            ?? throw new KeyNotFoundException("User not found.");

        if (!user.IsActive)
            throw new InvalidOperationException("User account is inactive.");

        var followerCount = await _context.Follows.CountAsync(f => f.FollowingId == userId);
        var followingCount = await _context.Follows.CountAsync(f => f.FollowerId == userId);
        var postCount = await _context.Posts.CountAsync(p => p.UserId == userId && !p.IsDeleted);

        bool isFollowing = false;
        if (currentUserId.HasValue && currentUserId.Value != userId)
        {
            isFollowing = await _context.Follows.AnyAsync(f => f.FollowerId == currentUserId.Value && f.FollowingId == userId);
        }

        return new PublicUserProfileDto
        {
            Id = user.Id,
            Username = user.Username,
            DisplayName = user.DisplayName,
            AvatarUrl = user.AvatarUrl,
            Bio = user.Bio,
            FollowerCount = followerCount,
            FollowingCount = followingCount,
            PostCount = postCount,
            IsFollowing = isFollowing,
            CreatedAt = user.CreatedAt
        };
    }

    public async Task<PublicUserProfileDto?> GetByUsernameAsync(string username, Guid? currentUserId = null)
    {
        var cleanUsername = username.TrimStart('@').Trim().ToLower();
        var user = await _context.Users.FirstOrDefaultAsync(u => u.Username.ToLower() == cleanUsername);
        if (user == null || !user.IsActive)
            return null;

        var followerCount = await _context.Follows.CountAsync(f => f.FollowingId == user.Id);
        var followingCount = await _context.Follows.CountAsync(f => f.FollowerId == user.Id);
        var postCount = await _context.Posts.CountAsync(p => p.UserId == user.Id && !p.IsDeleted);

        bool isFollowing = false;
        if (currentUserId.HasValue && currentUserId.Value != user.Id)
        {
            isFollowing = await _context.Follows.AnyAsync(f => f.FollowerId == currentUserId.Value && f.FollowingId == user.Id);
        }

        return new PublicUserProfileDto
        {
            Id = user.Id,
            Username = user.Username,
            DisplayName = user.DisplayName,
            AvatarUrl = user.AvatarUrl,
            Bio = user.Bio,
            FollowerCount = followerCount,
            FollowingCount = followingCount,
            PostCount = postCount,
            IsFollowing = isFollowing,
            CreatedAt = user.CreatedAt
        };
    }

    public async Task<List<PublicUserProfileDto>> SearchUsersAsync(string query, Guid? currentUserId = null, int limit = 10)
    {
        var clean = query.TrimStart('@').Trim().ToLower();
        if (string.IsNullOrWhiteSpace(clean))
            return new List<PublicUserProfileDto>();

        var users = await _context.Users
            .Where(u => u.IsActive && (u.Username.ToLower().Contains(clean) || u.DisplayName.ToLower().Contains(clean)))
            .Take(limit)
            .ToListAsync();

        var results = new List<PublicUserProfileDto>();
        foreach (var user in users)
        {
            bool isFollowing = false;
            if (currentUserId.HasValue && currentUserId.Value != user.Id)
            {
                isFollowing = await _context.Follows.AnyAsync(f => f.FollowerId == currentUserId.Value && f.FollowingId == user.Id);
            }

            results.Add(new PublicUserProfileDto
            {
                Id = user.Id,
                Username = user.Username,
                DisplayName = user.DisplayName,
                AvatarUrl = user.AvatarUrl,
                Bio = user.Bio,
                IsFollowing = isFollowing,
                CreatedAt = user.CreatedAt
            });
        }
        return results;
    }

    public async Task<UserDto> UpdateProfileAsync(Guid userId, UpdateUserRequest request)
    {
        var user = await _context.Users.FindAsync(userId)
            ?? throw new KeyNotFoundException("User not found.");

        if (request.DisplayName != null) user.DisplayName = request.DisplayName;
        if (request.Bio != null) user.Bio = request.Bio;
        if (request.AvatarUrl != null) user.AvatarUrl = request.AvatarUrl;

        await _context.SaveChangesAsync();
        return await MapToDto(user);
    }

    public async Task FollowAsync(Guid followerId, Guid followingId)
    {
        if (followerId == followingId)
            throw new InvalidOperationException("Cannot follow yourself.");

        var exists = await _context.Follows
            .AnyAsync(f => f.FollowerId == followerId && f.FollowingId == followingId);
        if (exists)
            throw new InvalidOperationException("Already following this user.");

        var targetUser = await _context.Users.FindAsync(followingId)
            ?? throw new KeyNotFoundException("User not found.");

        _context.Follows.Add(new Follow
        {
            FollowerId = followerId,
            FollowingId = followingId
        });
        await _context.SaveChangesAsync();

        if (_notificationService != null && followerId != followingId)
        {
            var follower = await _context.Users.FindAsync(followerId);
            var followerName = follower?.DisplayName ?? follower?.Username ?? "Someone";
            await _notificationService.CreateAsync(
                followingId,
                NotificationType.NewFollower,
                "New Follower",
                $"{followerName} started following you.",
                followerId);
        }
    }

    public async Task UnfollowAsync(Guid followerId, Guid followingId)
    {
        var follow = await _context.Follows
            .FirstOrDefaultAsync(f => f.FollowerId == followerId && f.FollowingId == followingId)
            ?? throw new KeyNotFoundException("Not following this user.");

        _context.Follows.Remove(follow);
        await _context.SaveChangesAsync();
    }

    public async Task<PagedResult<UserDto>> GetFollowersAsync(Guid userId, PaginationParams pagination)
    {
        var query = _context.Follows
            .Where(f => f.FollowingId == userId)
            .Select(f => f.Follower);

        var totalItems = await query.CountAsync();
        var users = await query
            .OrderByDescending(u => u.CreatedAt)
            .Skip((pagination.Page - 1) * pagination.PageSize)
            .Take(pagination.PageSize)
            .ToListAsync();

        var dtos = new List<UserDto>();
        foreach (var user in users)
            dtos.Add(await MapToDto(user));

        return new PagedResult<UserDto>
        {
            Items = dtos,
            Page = pagination.Page,
            PageSize = pagination.PageSize,
            TotalItems = totalItems
        };
    }

    public async Task<PagedResult<UserDto>> GetFollowingAsync(Guid userId, PaginationParams pagination)
    {
        var query = _context.Follows
            .Where(f => f.FollowerId == userId)
            .Select(f => f.Following);

        var totalItems = await query.CountAsync();
        var users = await query
            .OrderByDescending(u => u.CreatedAt)
            .Skip((pagination.Page - 1) * pagination.PageSize)
            .Take(pagination.PageSize)
            .ToListAsync();

        var dtos = new List<UserDto>();
        foreach (var user in users)
            dtos.Add(await MapToDto(user));

        return new PagedResult<UserDto>
        {
            Items = dtos,
            Page = pagination.Page,
            PageSize = pagination.PageSize,
            TotalItems = totalItems
        };
    }

    public async Task<bool> IsFollowingAsync(Guid followerId, Guid followingId)
    {
        return await _context.Follows
            .AnyAsync(f => f.FollowerId == followerId && f.FollowingId == followingId);
    }

    public async Task<UserDto> UpdateAvatarAsync(Guid userId, Stream fileStream, string fileName, string contentType, CancellationToken cancellationToken = default)
    {
        if (_mediaStorageService == null || _fileValidator == null)
            throw new InvalidOperationException("Media storage or file validator service is not configured.");

        var validation = _fileValidator.Validate(fileStream, fileName, contentType, AllowedMediaType.Image);
        if (!validation.IsValid)
            throw new ArgumentException(validation.ErrorMessage ?? "Invalid avatar image.");

        var user = await _context.Users.FindAsync(new object[] { userId }, cancellationToken)
            ?? throw new KeyNotFoundException("User not found.");

        var oldAvatarUrl = user.AvatarUrl;
        Stream uploadStream = fileStream;
        string uploadContentType = validation.NormalizedContentType ?? contentType;
        string extension = validation.DetectedExtension ?? Path.GetExtension(fileName).ToLowerInvariant();

        ProcessedImageResult? processed = null;
        if (_imageProcessingService != null)
        {
            processed = await _imageProcessingService.ProcessAvatarAsync(fileStream, cancellationToken);
            uploadStream = processed.Stream;
            uploadContentType = processed.ContentType;
            extension = processed.Extension;
        }

        var key = $"avatars/{userId}_{Guid.NewGuid():N}{extension}";

        try
        {
            await _mediaStorageService.UploadAsync(uploadStream, key, uploadContentType, cancellationToken);
        }
        finally
        {
            processed?.Stream.Dispose();
        }

        try
        {
            user.AvatarUrl = _mediaStorageService.GetUrl(key);
            user.UpdatedAt = DateTime.UtcNow;
            await _context.SaveChangesAsync(cancellationToken);
        }
        catch
        {
            await _mediaStorageService.DeleteAsync(key, cancellationToken);
            throw;
        }

        if (!string.IsNullOrWhiteSpace(oldAvatarUrl))
        {
            try
            {
                var oldKey = _mediaStorageService.GetKeyFromUrl(oldAvatarUrl);
                if (!string.IsNullOrWhiteSpace(oldKey))
                {
                    await _mediaStorageService.DeleteAsync(oldKey, cancellationToken);
                }
            }
            catch
            {
                // Best-effort cleanup of previous avatar file
            }
        }

        return await MapToDto(user);
    }

    private async Task<UserDto> MapToDto(User user)
    {
        return new UserDto
        {
            Id = user.Id,
            Username = user.Username,
            Email = user.Email,
            DisplayName = user.DisplayName,
            AvatarUrl = user.AvatarUrl,
            Bio = user.Bio,
            Role = user.Role.ToString(),
            IsActive = user.IsActive,
            FollowerCount = await _context.Follows.CountAsync(f => f.FollowingId == user.Id),
            FollowingCount = await _context.Follows.CountAsync(f => f.FollowerId == user.Id),
            PostCount = await _context.Posts.CountAsync(p => p.UserId == user.Id),
            CreatedAt = user.CreatedAt
        };
    }
}
