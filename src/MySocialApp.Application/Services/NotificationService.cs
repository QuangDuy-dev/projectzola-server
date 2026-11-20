using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using MySocialApp.Application.DTOs.Common;
using MySocialApp.Application.DTOs.Notifications;
using MySocialApp.Application.Interfaces;
using MySocialApp.Domain.Entities;
using MySocialApp.Domain.Enums;

namespace MySocialApp.Application.Services;

public class NotificationService : INotificationService
{
    private readonly IApplicationDbContext _context;
    private readonly IRealtimeNotificationSender _realtimeSender;
    private readonly ILogger<NotificationService> _logger;

    public NotificationService(
        IApplicationDbContext context,
        IRealtimeNotificationSender realtimeSender,
        ILogger<NotificationService> logger)
    {
        _context = context;
        _realtimeSender = realtimeSender;
        _logger = logger;
    }

    public async Task<NotificationDto> CreateAsync(
        Guid userId,
        NotificationType type,
        string title,
        string content,
        Guid? relatedEntityId = null)
    {
        var notification = new Notification
        {
            UserId = userId,
            Type = type,
            Title = title,
            Content = content,
            RelatedEntityId = relatedEntityId,
            IsRead = false
        };

        _context.Notifications.Add(notification);
        await _context.SaveChangesAsync();

        var dto = MapToDto(notification);

        // Realtime delivery (resilient - log and do not rollback business operation on failure)
        try
        {
            var unreadCount = await _context.Notifications.CountAsync(n => n.UserId == userId && !n.IsRead);
            await _realtimeSender.SendNotificationAsync(userId, dto);
            await _realtimeSender.SendUnreadCountAsync(userId, unreadCount);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to broadcast realtime notification to user {UserId}", userId);
        }

        return dto;
    }

    public async Task<NotificationDto> CreateOrUpdateLikeNotificationAsync(
        Guid targetUserId,
        Guid actorUserId,
        Guid postId)
    {
        // Do not notify self
        if (targetUserId == actorUserId)
            return null!;

        var actor = await _context.Users.FindAsync(actorUserId);
        var actorName = actor?.DisplayName ?? actor?.Username ?? "Someone";

        // Like Deduplication Strategy:
        // If an unread PostLiked notification already exists for this post and recipient,
        // refresh its CreatedAt and re-broadcast instead of creating unbounded duplicate rows.
        var existing = await _context.Notifications
            .FirstOrDefaultAsync(n => n.UserId == targetUserId
                                      && n.Type == NotificationType.PostLiked
                                      && n.RelatedEntityId == postId
                                      && !n.IsRead);

        if (existing != null)
        {
            existing.Title = "New Like";
            existing.Content = $"{actorName} liked your post.";
            existing.CreatedAt = DateTime.UtcNow;
            await _context.SaveChangesAsync();

            var updatedDto = MapToDto(existing);

            try
            {
                var count = await _context.Notifications.CountAsync(n => n.UserId == targetUserId && !n.IsRead);
                await _realtimeSender.SendNotificationAsync(targetUserId, updatedDto);
                await _realtimeSender.SendUnreadCountAsync(targetUserId, count);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to broadcast deduplicated like notification to user {UserId}", targetUserId);
            }

            return updatedDto;
        }

        return await CreateAsync(
            targetUserId,
            NotificationType.PostLiked,
            "New Like",
            $"{actorName} liked your post.",
            postId);
    }

    public async Task<PagedResult<NotificationDto>> GetByUserAsync(Guid userId, PaginationParams pagination)
    {
        var query = _context.Notifications
            .Where(n => n.UserId == userId)
            .OrderByDescending(n => n.CreatedAt);

        var totalItems = await query.CountAsync();
        var notifications = await query
            .Skip((pagination.Page - 1) * pagination.PageSize)
            .Take(pagination.PageSize)
            .ToListAsync();

        return new PagedResult<NotificationDto>
        {
            Items = notifications.Select(MapToDto).ToList(),
            Page = pagination.Page,
            PageSize = pagination.PageSize,
            TotalItems = totalItems
        };
    }

    public async Task<int> GetUnreadCountAsync(Guid userId)
    {
        return await _context.Notifications
            .CountAsync(n => n.UserId == userId && !n.IsRead);
    }

    public async Task<NotificationDto> MarkAsReadAsync(Guid notificationId, Guid userId)
    {
        var notification = await _context.Notifications
            .FirstOrDefaultAsync(n => n.Id == notificationId && n.UserId == userId)
            ?? throw new KeyNotFoundException("Notification not found.");

        if (!notification.IsRead)
        {
            notification.IsRead = true;
            notification.ReadAt = DateTime.UtcNow;
            await _context.SaveChangesAsync();
        }

        var dto = MapToDto(notification);

        try
        {
            var unreadCount = await _context.Notifications.CountAsync(n => n.UserId == userId && !n.IsRead);
            await _realtimeSender.SendNotificationReadAsync(userId, new NotificationReadEventDto(notification.Id, true, notification.ReadAt));
            await _realtimeSender.SendUnreadCountAsync(userId, unreadCount);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to broadcast notification read event to user {UserId}", userId);
        }

        return dto;
    }

    public async Task MarkAllAsReadAsync(Guid userId)
    {
        var unreadNotifications = await _context.Notifications
            .Where(n => n.UserId == userId && !n.IsRead)
            .ToListAsync();

        if (unreadNotifications.Count > 0)
        {
            var now = DateTime.UtcNow;
            foreach (var n in unreadNotifications)
            {
                n.IsRead = true;
                n.ReadAt = now;
            }
            await _context.SaveChangesAsync();
        }

        try
        {
            await _realtimeSender.SendUnreadCountAsync(userId, 0);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to broadcast mark-all-read event to user {UserId}", userId);
        }
    }

    private static NotificationDto MapToDto(Notification n)
    {
        return new NotificationDto
        {
            Id = n.Id,
            Type = n.Type.ToString(),
            Title = n.Title,
            Content = n.Content,
            RelatedEntityId = n.RelatedEntityId,
            IsRead = n.IsRead,
            CreatedAt = n.CreatedAt,
            ReadAt = n.ReadAt
        };
    }
}
