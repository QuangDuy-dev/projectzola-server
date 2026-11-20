using MySocialApp.Application.DTOs.Common;
using MySocialApp.Application.DTOs.Notifications;
using MySocialApp.Domain.Enums;

namespace MySocialApp.Application.Interfaces;

public interface INotificationService
{
    Task<NotificationDto> CreateAsync(Guid userId, NotificationType type, string title, string content, Guid? relatedEntityId = null);
    Task<NotificationDto> CreateOrUpdateLikeNotificationAsync(Guid targetUserId, Guid actorUserId, Guid postId);
    Task<PagedResult<NotificationDto>> GetByUserAsync(Guid userId, PaginationParams pagination);
    Task<int> GetUnreadCountAsync(Guid userId);
    Task<NotificationDto> MarkAsReadAsync(Guid notificationId, Guid userId);
    Task MarkAllAsReadAsync(Guid userId);
}
