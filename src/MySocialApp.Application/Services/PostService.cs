using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using MySocialApp.Application.DTOs.Common;
using MySocialApp.Application.DTOs.Posts;
using MySocialApp.Application.Interfaces;
using MySocialApp.Application.Options;
using MySocialApp.Domain.Entities;
using MySocialApp.Domain.Enums;

namespace MySocialApp.Application.Services;

public class PostService : IPostService
{
    private readonly IApplicationDbContext _context;
    private readonly IMediaStorageService? _mediaStorageService;
    private readonly IFileValidator? _fileValidator;
    private readonly IMediaProcessingService? _mediaProcessingService;
    private readonly IImageProcessingService? _imageProcessingService;
    private readonly IOptions<MediaStorageOptions>? _storageOptions;
    private readonly INotificationService? _notificationService;

    public PostService(
        IApplicationDbContext context,
        IMediaStorageService? mediaStorageService = null,
        IFileValidator? fileValidator = null,
        IMediaProcessingService? mediaProcessingService = null,
        IOptions<MediaStorageOptions>? storageOptions = null,
        IImageProcessingService? imageProcessingService = null,
        INotificationService? notificationService = null)
    {
        _context = context;
        _mediaStorageService = mediaStorageService;
        _fileValidator = fileValidator;
        _mediaProcessingService = mediaProcessingService;
        _storageOptions = storageOptions;
        _imageProcessingService = imageProcessingService;
        _notificationService = notificationService;
    }

    public async Task<PagedResult<PostDto>> GetPostsAsync(PaginationParams pagination, Guid? currentUserId = null)
    {
        var query = _context.Posts
            .Include(p => p.User)
            .Include(p => p.Media)
            .Include(p => p.Reactions)
            .Include(p => p.Comments)
            .OrderByDescending(p => p.CreatedAt);

        var totalItems = await query.CountAsync();
        var posts = await query
            .Skip((pagination.Page - 1) * pagination.PageSize)
            .Take(pagination.PageSize)
            .ToListAsync();

        return new PagedResult<PostDto>
        {
            Items = posts.Select(p => MapToDto(p, currentUserId)).ToList(),
            Page = pagination.Page,
            PageSize = pagination.PageSize,
            TotalItems = totalItems
        };
    }

    public async Task<PostDto> GetByIdAsync(Guid id, Guid? currentUserId = null)
    {
        var post = await _context.Posts
            .Include(p => p.User)
            .Include(p => p.Media)
            .Include(p => p.Reactions)
            .Include(p => p.Comments)
            .FirstOrDefaultAsync(p => p.Id == id)
            ?? throw new KeyNotFoundException("Post not found.");

        return MapToDto(post, currentUserId);
    }

    public async Task<PostDto> CreateAsync(Guid userId, CreatePostRequest request)
    {
        var post = new Post
        {
            UserId = userId,
            Content = request.Content
        };

        _context.Posts.Add(post);
        await _context.SaveChangesAsync();

        return await GetByIdAsync(post.Id, userId);
    }

    public async Task<PostDto> UpdateAsync(Guid postId, Guid userId, UpdatePostRequest request)
    {
        var post = await _context.Posts.FindAsync(postId)
            ?? throw new KeyNotFoundException("Post not found.");

        if (post.UserId != userId)
            throw new UnauthorizedAccessException("Not authorized to update this post.");

        post.Content = request.Content;
        await _context.SaveChangesAsync();

        return await GetByIdAsync(postId, userId);
    }

    public async Task DeleteAsync(Guid postId, Guid userId)
    {
        var post = await _context.Posts.FindAsync(postId)
            ?? throw new KeyNotFoundException("Post not found.");

        if (post.UserId != userId)
            throw new UnauthorizedAccessException("Not authorized to delete this post.");

        post.IsDeleted = true;
        await _context.SaveChangesAsync();
    }

    public async Task<PagedResult<PostDto>> GetUserPostsAsync(Guid userId, PaginationParams pagination, Guid? currentUserId = null)
    {
        var query = _context.Posts
            .Include(p => p.User)
            .Include(p => p.Media)
            .Include(p => p.Reactions)
            .Include(p => p.Comments)
            .Where(p => p.UserId == userId)
            .OrderByDescending(p => p.CreatedAt);

        var totalItems = await query.CountAsync();
        var posts = await query
            .Skip((pagination.Page - 1) * pagination.PageSize)
            .Take(pagination.PageSize)
            .ToListAsync();

        return new PagedResult<PostDto>
        {
            Items = posts.Select(p => MapToDto(p, currentUserId)).ToList(),
            Page = pagination.Page,
            PageSize = pagination.PageSize,
            TotalItems = totalItems
        };
    }

    public async Task<string?> ToggleReactionAsync(Guid postId, Guid userId, string reactionType)
    {
        if (!Enum.TryParse<ReactionType>(reactionType, true, out var type))
            throw new InvalidOperationException("Invalid reaction type. Must be 'Like' or 'Dislike'.");

        var post = await _context.Posts.FindAsync(postId)
            ?? throw new KeyNotFoundException("Post not found.");

        var existing = await _context.PostReactions
            .FirstOrDefaultAsync(r => r.PostId == postId && r.UserId == userId);

        if (existing != null)
        {
            if (existing.ReactionType == type)
            {
                // Toggle off - same reaction removes it
                _context.PostReactions.Remove(existing);
                await _context.SaveChangesAsync();
                return null;
            }
            else
            {
                // Switch reaction type
                existing.ReactionType = type;
                existing.UpdatedAt = DateTime.UtcNow;
                await _context.SaveChangesAsync();

                if (type == ReactionType.Like && _notificationService != null && post.UserId != userId)
                {
                    await _notificationService.CreateOrUpdateLikeNotificationAsync(post.UserId, userId, postId);
                }

                return type.ToString();
            }
        }

        // New reaction
        _context.PostReactions.Add(new PostReaction
        {
            PostId = postId,
            UserId = userId,
            ReactionType = type
        });
        await _context.SaveChangesAsync();

        if (type == ReactionType.Like && _notificationService != null && post.UserId != userId)
        {
            await _notificationService.CreateOrUpdateLikeNotificationAsync(post.UserId, userId, postId);
        }

        return type.ToString();
    }

    public async Task RemoveReactionAsync(Guid postId, Guid userId)
    {
        var existing = await _context.PostReactions
            .FirstOrDefaultAsync(r => r.PostId == postId && r.UserId == userId)
            ?? throw new KeyNotFoundException("Reaction not found.");

        _context.PostReactions.Remove(existing);
        await _context.SaveChangesAsync();
    }

    public async Task<PostMediaDto> AddMediaAsync(Guid postId, Guid userId, Stream fileStream, string fileName, string contentType, CancellationToken cancellationToken = default)
    {
        if (_mediaStorageService == null || _fileValidator == null)
            throw new InvalidOperationException("Media storage or file validator service is not configured.");

        var validation = _fileValidator.Validate(fileStream, fileName, contentType, AllowedMediaType.Any);
        if (!validation.IsValid)
            throw new ArgumentException(validation.ErrorMessage ?? "Invalid media file.");

        var post = await _context.Posts
            .Include(p => p.Media)
            .FirstOrDefaultAsync(p => p.Id == postId, cancellationToken)
            ?? throw new KeyNotFoundException("Post not found.");

        if (post.UserId != userId)
            throw new UnauthorizedAccessException("Not authorized to add media to this post.");

        if (validation.IsVideo)
        {
            if (post.Media.Any(m => m.MediaType == MediaType.Video))
                throw new InvalidOperationException("A post can only contain a maximum of 1 video.");

            bool requireFFmpeg = _storageOptions?.Value.RequireFFmpeg ?? false;
            bool isFfmpegAvailable = _mediaProcessingService?.IsFFmpegAvailable ?? false;

            if (requireFFmpeg && !isFfmpegAvailable)
            {
                throw new InvalidOperationException("FFmpeg/ffprobe is required for video processing but is not available on this server.");
            }
        }
        else
        {
            if (post.Media.Count(m => m.MediaType == MediaType.Image) >= 10)
                throw new InvalidOperationException("A post cannot contain more than 10 images.");
        }

        Stream uploadStream = fileStream;
        string uploadContentType = validation.NormalizedContentType ?? contentType;
        string extension = validation.DetectedExtension ?? Path.GetExtension(fileName).ToLowerInvariant();
        ProcessedImageResult? processedImage = null;

        if (!validation.IsVideo && _imageProcessingService != null)
        {
            processedImage = await _imageProcessingService.ProcessImageAsync(fileStream,
                _storageOptions?.Value.ImageMaxDimension ?? 1920, _storageOptions?.Value.ImageQuality ?? 80, cancellationToken);
            uploadStream = processedImage.Stream;
            uploadContentType = processedImage.ContentType;
            extension = processedImage.Extension;
        }

        var folder = validation.IsVideo ? "posts/videos" : "posts/images";
        var storageKey = $"{folder}/{postId}_{Guid.NewGuid():N}{extension}";

        try
        {
            await _mediaStorageService.UploadAsync(uploadStream, storageKey, uploadContentType, cancellationToken);
        }
        catch
        {
            await _mediaStorageService.DeleteAsync(storageKey, CancellationToken.None);
            throw;
        }
        finally
        {
            processedImage?.Stream.Dispose();
        }

        string? thumbnailKey = null;

        string? candidateKey = null;
        try
        {
            if (validation.IsVideo && _mediaProcessingService != null && _storageOptions != null)
            {
                var physicalVideoPath = Path.Combine(_storageOptions.Value.RootPath, storageKey);
                if (_mediaProcessingService.IsFFmpegAvailable)
                {
                    var inspection = await _mediaProcessingService.InspectVideoAsync(physicalVideoPath, cancellationToken);
                    if (_storageOptions.Value.RequireFFmpeg && !inspection.IsInspected)
                        throw new InvalidOperationException("Cannot inspect this video. Please upload a valid MP4 or WEBM file.");
                    var maxDuration = _storageOptions.Value.VideoMaxDurationSeconds;
                    if (inspection.IsInspected && inspection.DurationSeconds > maxDuration)
                        throw new InvalidOperationException($"Video duration ({inspection.DurationSeconds:F1}s) exceeds the maximum allowed duration of {maxDuration} seconds.");

                    if (inspection.IsInspected)
                    {
                        candidateKey = $"posts/videos/{postId}_{Guid.NewGuid():N}.mp4";
                        var candidatePath = Path.Combine(_storageOptions.Value.RootPath, candidateKey);
                        var transcoded = await _mediaProcessingService.TranscodeVideoAsync(physicalVideoPath, candidatePath, cancellationToken);
                        // Re-encoding a compact AV1/WebM can be larger. Never replace a
                        // source with a larger/equal result or a corrupt partial output.
                        if (transcoded != null && File.Exists(candidatePath) &&
                            new FileInfo(candidatePath).Length > 0 &&
                            new FileInfo(candidatePath).Length < new FileInfo(physicalVideoPath).Length)
                        {
                            var verified = await _mediaProcessingService.InspectVideoAsync(candidatePath, cancellationToken);
                            if (verified.IsInspected && verified.DurationSeconds.HasValue && inspection.DurationSeconds.HasValue &&
                                Math.Abs(verified.DurationSeconds.Value - inspection.DurationSeconds.Value) < 0.5)
                            {
                                await _mediaStorageService.DeleteAsync(storageKey, cancellationToken);
                                storageKey = candidateKey;
                                physicalVideoPath = candidatePath;
                                candidateKey = null;
                            }
                        }
                        if (candidateKey != null)
                        {
                            await _mediaStorageService.DeleteAsync(candidateKey, CancellationToken.None);
                            candidateKey = null;
                        }
                        thumbnailKey = $"thumbnails/{postId}_{Guid.NewGuid():N}.jpg";
                        var thumbnail = await _mediaProcessingService.ExtractVideoThumbnailAsync(physicalVideoPath,
                            Path.Combine(_storageOptions.Value.RootPath, thumbnailKey), cancellationToken);
                        if (thumbnail == null) thumbnailKey = null;
                    }
                }
            }
        }
        catch
        {
            await _mediaStorageService.DeleteAsync(storageKey, CancellationToken.None);
            if (candidateKey != null) await _mediaStorageService.DeleteAsync(candidateKey, CancellationToken.None);
            if (thumbnailKey != null) await _mediaStorageService.DeleteAsync(thumbnailKey, CancellationToken.None);
            throw;
        }

        var postMedia = new PostMedia
        {
            PostId = postId,
            MediaType = validation.IsVideo ? MediaType.Video : MediaType.Image,
            StorageUrl = _mediaStorageService.GetUrl(storageKey),
            ThumbnailUrl = thumbnailKey != null ? _mediaStorageService.GetUrl(thumbnailKey) : null
        };

        _context.PostMedia.Add(postMedia);

        try
        {
            await _context.SaveChangesAsync(cancellationToken);
        }
        catch
        {
            await _mediaStorageService.DeleteAsync(storageKey, CancellationToken.None);
            if (thumbnailKey != null)
            {
                await _mediaStorageService.DeleteAsync(thumbnailKey, CancellationToken.None);
            }
            throw;
        }

        return new PostMediaDto
        {
            Id = postMedia.Id,
            MediaType = postMedia.MediaType.ToString(),
            StorageUrl = postMedia.StorageUrl,
            ThumbnailUrl = postMedia.ThumbnailUrl
        };
    }

    public async Task DeleteMediaAsync(Guid postId, Guid mediaId, Guid userId, CancellationToken cancellationToken = default)
    {
        var post = await _context.Posts.FindAsync(new object[] { postId }, cancellationToken)
            ?? throw new KeyNotFoundException("Post not found.");

        if (post.UserId != userId)
            throw new UnauthorizedAccessException("Not authorized to modify this post.");

        var media = await _context.PostMedia.FirstOrDefaultAsync(m => m.Id == mediaId && m.PostId == postId, cancellationToken)
            ?? throw new KeyNotFoundException("Media not found.");

        _context.PostMedia.Remove(media);
        await _context.SaveChangesAsync(cancellationToken);

        if (_mediaStorageService != null)
        {
            var key = _mediaStorageService.GetKeyFromUrl(media.StorageUrl);
            if (!string.IsNullOrWhiteSpace(key))
            {
                await _mediaStorageService.DeleteAsync(key, cancellationToken);
            }

            if (!string.IsNullOrWhiteSpace(media.ThumbnailUrl))
            {
                var thumbKey = _mediaStorageService.GetKeyFromUrl(media.ThumbnailUrl);
                if (!string.IsNullOrWhiteSpace(thumbKey))
                {
                    await _mediaStorageService.DeleteAsync(thumbKey, cancellationToken);
                }
            }
        }
    }

    private static PostDto MapToDto(Post post, Guid? currentUserId)
    {
        return new PostDto
        {
            Id = post.Id,
            UserId = post.UserId,
            Username = post.User.Username,
            DisplayName = post.User.DisplayName,
            UserAvatarUrl = post.User.AvatarUrl,
            Content = post.Content,
            LikeCount = post.Reactions.Count(r => r.ReactionType == ReactionType.Like),
            DislikeCount = post.Reactions.Count(r => r.ReactionType == ReactionType.Dislike),
            CommentCount = post.Comments.Count,
            CurrentUserReaction = currentUserId.HasValue
                ? post.Reactions.FirstOrDefault(r => r.UserId == currentUserId)?.ReactionType.ToString()
                : null,
            Media = post.Media.Select(m => new PostMediaDto
            {
                Id = m.Id,
                MediaType = m.MediaType.ToString(),
                StorageUrl = m.StorageUrl,
                ThumbnailUrl = m.ThumbnailUrl
            }).ToList(),
            CreatedAt = post.CreatedAt,
            UpdatedAt = post.UpdatedAt
        };
    }
}
