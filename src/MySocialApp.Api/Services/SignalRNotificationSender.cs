using Microsoft.AspNetCore.SignalR;
using MySocialApp.Api.Hubs;
using MySocialApp.Application.DTOs.Notifications;
using MySocialApp.Application.Interfaces;

namespace MySocialApp.Api.Services;

public class SignalRNotificationSender : IRealtimeNotificationSender
{
    private readonly IHubContext<ChatHub, IChatClient> _hubContext;
    private readonly ILogger<SignalRNotificationSender> _logger;

    public SignalRNotificationSender(
        IHubContext<ChatHub, IChatClient> hubContext,
        ILogger<SignalRNotificationSender> logger)
    {
        _hubContext = hubContext;
        _logger = logger;
    }

    public async Task SendNotificationAsync(Guid userId, NotificationDto notification)
    {
        try
        {
            await _hubContext.Clients.Group($"user:{userId}").NotificationReceived(notification);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to deliver SignalR NotificationReceived to user group user:{UserId}", userId);
        }
    }

    public async Task SendNotificationReadAsync(Guid userId, NotificationReadEventDto eventData)
    {
        try
        {
            await _hubContext.Clients.Group($"user:{userId}").NotificationRead(eventData);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to deliver SignalR NotificationRead to user group user:{UserId}", userId);
        }
    }

    public async Task SendUnreadCountAsync(Guid userId, int unreadCount)
    {
        try
        {
            await _hubContext.Clients.Group($"user:{userId}").NotificationCountChanged(new NotificationCountDto(unreadCount));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to deliver SignalR NotificationCountChanged to user group user:{UserId}", userId);
        }
    }
}
