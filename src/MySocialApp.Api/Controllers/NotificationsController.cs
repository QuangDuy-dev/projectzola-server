using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using MySocialApp.Application.DTOs.Common;
using MySocialApp.Application.DTOs.Notifications;
using MySocialApp.Application.Interfaces;

namespace MySocialApp.Api.Controllers;

[ApiController]
[Route("api/notifications")]
[Authorize]
public class NotificationsController : ControllerBase
{
    private readonly INotificationService _notificationService;

    public NotificationsController(INotificationService notificationService)
    {
        _notificationService = notificationService;
    }

    private Guid GetCurrentUserId()
    {
        return Guid.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);
    }

    [HttpGet]
    public async Task<ActionResult<ApiResponse<PagedResult<NotificationDto>>>> GetAll([FromQuery] PaginationParams pagination)
    {
        var userId = GetCurrentUserId();
        var result = await _notificationService.GetByUserAsync(userId, pagination);
        return Ok(ApiResponse<PagedResult<NotificationDto>>.SuccessResponse(result));
    }

    [HttpGet("unread-count")]
    public async Task<ActionResult<ApiResponse<NotificationCountDto>>> GetUnreadCount()
    {
        var userId = GetCurrentUserId();
        var count = await _notificationService.GetUnreadCountAsync(userId);
        return Ok(ApiResponse<NotificationCountDto>.SuccessResponse(new NotificationCountDto(count)));
    }

    [HttpPost("{id}/read")]
    public async Task<ActionResult<ApiResponse<NotificationDto>>> MarkAsRead(Guid id)
    {
        var userId = GetCurrentUserId();
        var result = await _notificationService.MarkAsReadAsync(id, userId);
        return Ok(ApiResponse<NotificationDto>.SuccessResponse(result, "Notification marked as read."));
    }

    [HttpPost("read-all")]
    public async Task<ActionResult<ApiResponse>> MarkAllAsRead()
    {
        var userId = GetCurrentUserId();
        await _notificationService.MarkAllAsReadAsync(userId);
        return Ok(ApiResponse.SuccessResponse("All notifications marked as read."));
    }
}
