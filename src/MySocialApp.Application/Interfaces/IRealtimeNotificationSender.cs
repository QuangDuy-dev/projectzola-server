using MySocialApp.Application.DTOs.Notifications;

namespace MySocialApp.Application.Interfaces;

public interface IRealtimeNotificationSender
{
    Task SendNotificationAsync(Guid userId, NotificationDto notification);
    Task SendNotificationReadAsync(Guid userId, NotificationReadEventDto eventData);
    Task SendUnreadCountAsync(Guid userId, int unreadCount);
}
