namespace MySocialApp.Application.DTOs.Notifications;

public record NotificationReadEventDto(Guid NotificationId, bool IsRead, DateTime? ReadAt);

public record NotificationCountDto(int UnreadCount);
