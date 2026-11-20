using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;
using MySocialApp.Application.DTOs.Call;
using MySocialApp.Application.DTOs.Chat;
using MySocialApp.Application.Interfaces;

using Microsoft.EntityFrameworkCore;

namespace MySocialApp.Api.Hubs;

[Authorize]
public class ChatHub : Hub<IChatClient>
{
    private readonly IChatService _chatService;
    private readonly ICallService _callService;
    private readonly IUserPresenceService _presenceService;
    private readonly IPresenceQueryService _presenceQueryService;
    private readonly IApplicationDbContext _context;
    private readonly ILogger<ChatHub> _logger;

    public ChatHub(
        IChatService chatService,
        ICallService callService,
        IUserPresenceService presenceService,
        IPresenceQueryService presenceQueryService,
        IApplicationDbContext context,
        ILogger<ChatHub> logger)
    {
        _chatService = chatService;
        _callService = callService;
        _presenceService = presenceService;
        _presenceQueryService = presenceQueryService;
        _context = context;
        _logger = logger;
    }

    private Guid GetUserId()
    {
        var idClaim = Context.User?.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        if (!Guid.TryParse(idClaim, out var userId))
        {
            throw new HubException("Invalid user identity.");
        }
        return userId;
    }

    private string GetUsername()
    {
        return Context.User?.Identity?.Name
            ?? Context.User?.FindFirst(ClaimTypes.Name)?.Value
            ?? "Unknown";
    }

    public override async Task OnConnectedAsync()
    {
        var userId = GetUserId();

        // Join personal user group for targeted multi-device events
        await Groups.AddToGroupAsync(Context.ConnectionId, $"user:{userId}");

        // Join groups for all conversations the user is currently part of
        var conversationIds = await _chatService.GetUserConversationIdsAsync(userId);
        foreach (var convId in conversationIds)
        {
            await Groups.AddToGroupAsync(Context.ConnectionId, $"conversation:{convId}");
        }

        // Update presence tracking
        var isNewlyOnline = await _presenceService.UserConnectedAsync(userId, Context.ConnectionId);
        if (isNewlyOnline)
        {
            _logger.LogInformation("User {UserId} is now ONLINE", userId);
            await Clients.Others.UserPresenceChanged(new UserPresenceDto(userId, true, DateTime.UtcNow));
        }

        await base.OnConnectedAsync();
    }

    public override async Task OnDisconnectedAsync(Exception? exception)
    {
        try
        {
            var userId = GetUserId();
            var isNewlyOffline = await _presenceService.UserDisconnectedAsync(userId, Context.ConnectionId);
            if (isNewlyOffline)
            {
                var offlineAt = DateTime.UtcNow;
                await _presenceQueryService.UpdateLastSeenAsync(userId);
                _logger.LogInformation("User {UserId} is now OFFLINE", userId);
                await Clients.Others.UserPresenceChanged(new UserPresenceDto(userId, false, offlineAt));

                var activeCall = await _callService.GetActiveCallForUserAsync(userId);
                if (activeCall != null)
                {
                    var (endedDto, msgDto) = await _callService.EndCallAsync(activeCall.Id, userId, "UserDisconnected");
                    var otherUserId = activeCall.CallerId == userId ? activeCall.CalleeId : activeCall.CallerId;
                    await Clients.Group($"user:{otherUserId}").CallEnded(endedDto);
                    if (msgDto != null)
                    {
                        await Clients.Group($"conversation:{activeCall.ConversationId}").ReceiveMessage(msgDto);
                        await Clients.Group($"user:{otherUserId}").ReceiveMessage(msgDto);
                    }
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error handling client disconnection");
        }

        await base.OnDisconnectedAsync(exception);
    }

    public async Task<MessageDto> SendMessage(Guid conversationId, SendMessageRequest request)
    {
        var userId = GetUserId();
        try
        {
            var message = await _chatService.SendMessageAsync(conversationId, userId, request);

            // Broadcast message to conversation group and individual member user groups
            var memberIds = await _context.ConversationMembers
                .Where(cm => cm.ConversationId == conversationId)
                .Select(cm => cm.UserId)
                .ToListAsync();

            var targetGroups = new List<string> { $"conversation:{conversationId}" };
            foreach (var mId in memberIds)
            {
                targetGroups.Add($"user:{mId}");
            }

            await Clients.Groups(targetGroups).ReceiveMessage(message);

            return message;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error sending message in conversation {ConversationId}", conversationId);
            throw new HubException(ex.Message);
        }
    }

    public async Task TypingStarted(Guid conversationId)
    {
        var userId = GetUserId();
        if (!await _chatService.IsMemberAsync(conversationId, userId))
            return;

        var username = GetUsername();
        var eventData = new TypingEventDto(conversationId, userId, username, true);
        await Clients.OthersInGroup($"conversation:{conversationId}").UserTyping(eventData);
    }

    public async Task TypingStopped(Guid conversationId)
    {
        var userId = GetUserId();
        if (!await _chatService.IsMemberAsync(conversationId, userId))
            return;

        var username = GetUsername();
        var eventData = new TypingEventDto(conversationId, userId, username, false);
        await Clients.OthersInGroup($"conversation:{conversationId}").UserTyping(eventData);
    }

    public async Task MarkDelivered(Guid conversationId, Guid messageId)
    {
        var userId = GetUserId();
        if (!await _chatService.IsMemberAsync(conversationId, userId))
            return;

        var eventData = new MessageDeliveryDto(messageId, conversationId, userId, DateTime.UtcNow);
        await Clients.OthersInGroup($"conversation:{conversationId}").MessageDelivered(eventData);
    }

    public async Task<MessageReadDto> MarkRead(Guid conversationId, MarkReadRequest request)
    {
        var userId = GetUserId();
        try
        {
            var readDto = await _chatService.MarkAsReadAsync(conversationId, userId, request);
            await Clients.Group($"conversation:{conversationId}").MessageRead(readDto);
            return readDto;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error marking message as read in conversation {ConversationId}", conversationId);
            throw new HubException(ex.Message);
        }
    }

    public async Task<MessageDto> EditMessage(Guid messageId, EditMessageRequest request)
    {
        var userId = GetUserId();
        try
        {
            var message = await _chatService.EditMessageAsync(messageId, userId, request);
            await Clients.Group($"conversation:{message.ConversationId}").MessageUpdated(message);
            return message;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error editing message {MessageId}", messageId);
            throw new HubException(ex.Message);
        }
    }

    public async Task<MessageDeletedDto> DeleteMessage(Guid messageId)
    {
        var userId = GetUserId();
        try
        {
            var deletedDto = await _chatService.DeleteMessageAsync(messageId, userId);
            await Clients.Group($"conversation:{deletedDto.ConversationId}").MessageDeleted(deletedDto);
            return deletedDto;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error deleting message {MessageId}", messageId);
            throw new HubException(ex.Message);
        }
    }

    public async Task JoinConversation(Guid conversationId)
    {
        var userId = GetUserId();
        if (await _chatService.IsMemberAsync(conversationId, userId))
        {
            await Groups.AddToGroupAsync(Context.ConnectionId, $"conversation:{conversationId}");
        }
    }

    public async Task LeaveConversation(Guid conversationId)
    {
        await Groups.RemoveFromGroupAsync(Context.ConnectionId, $"conversation:{conversationId}");
    }

    // ==========================================
    // WebRTC 1-1 Signaling Methods
    // ==========================================

    public async Task<CallSessionDto> StartCall(StartCallRequest request)
    {
        var callerId = GetUserId();
        try
        {
            var sessionDto = await _callService.StartCallAsync(callerId, request);

            if (sessionDto.Status == MySocialApp.Domain.Enums.CallStatus.Busy)
            {
                await Clients.Caller.CallBusy(sessionDto);
                return sessionDto;
            }

            // Notify callee on their personal group
            await Clients.Group($"user:{sessionDto.CalleeId}").CallRinging(sessionDto);
            // Confirm ringing state to caller
            await Clients.Caller.CallRinging(sessionDto);

            return sessionDto;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error starting call from user {CallerId} to {CalleeId}", callerId, request.CalleeId);
            throw new HubException(ex.Message);
        }
    }

    public async Task<CallSessionDto> AcceptCall(Guid callSessionId)
    {
        var calleeId = GetUserId();
        try
        {
            var sessionDto = await _callService.AcceptCallAsync(callSessionId, calleeId);

            // Notify both parties that call is accepted
            await Clients.Group($"user:{sessionDto.CallerId}").CallAccepted(sessionDto);
            await Clients.Caller.CallAccepted(sessionDto);

            return sessionDto;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error accepting call {CallSessionId} by user {CalleeId}", callSessionId, calleeId);
            throw new HubException(ex.Message);
        }
    }

    public async Task RejectCall(Guid callSessionId)
    {
        var userId = GetUserId();
        try
        {
            var session = await _callService.GetCallSessionAsync(callSessionId);
            if (session == null)
                throw new HubException("Phiên cuộc gọi không tồn tại.");

            var (endedDto, msgDto) = await _callService.RejectCallAsync(callSessionId, userId);

            await Clients.Group($"user:{session.CallerId}").CallRejected(endedDto);
            await Clients.Group($"user:{session.CalleeId}").CallRejected(endedDto);

            if (msgDto != null)
            {
                await Clients.Group($"conversation:{session.ConversationId}").ReceiveMessage(msgDto);
                await Clients.Group($"user:{session.CallerId}").ReceiveMessage(msgDto);
                await Clients.Group($"user:{session.CalleeId}").ReceiveMessage(msgDto);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error rejecting call {CallSessionId} by user {UserId}", callSessionId, userId);
            throw new HubException(ex.Message);
        }
    }

    public async Task EndCall(Guid callSessionId, string reason = "UserHangup")
    {
        var userId = GetUserId();
        try
        {
            var session = await _callService.GetCallSessionAsync(callSessionId);
            if (session == null)
                throw new HubException("Phiên cuộc gọi không tồn tại.");

            var (endedDto, msgDto) = await _callService.EndCallAsync(callSessionId, userId, reason);

            await Clients.Group($"user:{session.CallerId}").CallEnded(endedDto);
            await Clients.Group($"user:{session.CalleeId}").CallEnded(endedDto);

            if (msgDto != null)
            {
                await Clients.Group($"conversation:{session.ConversationId}").ReceiveMessage(msgDto);
                await Clients.Group($"user:{session.CallerId}").ReceiveMessage(msgDto);
                await Clients.Group($"user:{session.CalleeId}").ReceiveMessage(msgDto);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error ending call {CallSessionId} by user {UserId}", callSessionId, userId);
            throw new HubException(ex.Message);
        }
    }

    public async Task SendRtcOffer(SendRtcOfferRequest request)
    {
        var userId = GetUserId();
        try
        {
            var session = await _callService.GetCallSessionAsync(request.CallSessionId);
            if (session == null)
                throw new HubException("Phiên cuộc gọi không tồn tại.");

            if (session.CallerId != userId && session.CalleeId != userId)
                throw new HubException("Bạn không thuộc phiên cuộc gọi này.");

            var targetUserId = session.CallerId == userId ? session.CalleeId : session.CallerId;
            var caller = session.CallerId == userId ? session.Caller : session.Callee;

            var offerDto = new CallOfferDto(
                session.Id,
                session.ConversationId,
                userId,
                caller?.DisplayName ?? caller?.Username ?? "User",
                caller?.AvatarUrl,
                session.Type,
                request.SdpOffer
            );

            await Clients.Group($"user:{targetUserId}").ReceiveCallOffer(offerDto);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error forwarding RTC offer for session {CallSessionId}", request.CallSessionId);
            throw new HubException(ex.Message);
        }
    }

    public async Task SendRtcAnswer(SendRtcAnswerRequest request)
    {
        var userId = GetUserId();
        try
        {
            var session = await _callService.GetCallSessionAsync(request.CallSessionId);
            if (session == null)
                throw new HubException("Phiên cuộc gọi không tồn tại.");

            if (session.CallerId != userId && session.CalleeId != userId)
                throw new HubException("Bạn không thuộc phiên cuộc gọi này.");

            var targetUserId = session.CallerId == userId ? session.CalleeId : session.CallerId;

            var answerDto = new CallAnswerDto(
                session.Id,
                userId,
                request.SdpAnswer
            );

            await Clients.Group($"user:{targetUserId}").ReceiveCallAnswer(answerDto);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error forwarding RTC answer for session {CallSessionId}", request.CallSessionId);
            throw new HubException(ex.Message);
        }
    }

    public async Task SendRtcIceCandidate(SendRtcIceCandidateRequest request)
    {
        var userId = GetUserId();
        try
        {
            var session = await _callService.GetCallSessionAsync(request.CallSessionId);
            if (session == null)
                throw new HubException("Phiên cuộc gọi không tồn tại.");

            if (session.CallerId != userId && session.CalleeId != userId)
                throw new HubException("Bạn không thuộc phiên cuộc gọi này.");

            var targetUserId = session.CallerId == userId ? session.CalleeId : session.CallerId;

            var candidateDto = new CallIceCandidateDto(
                session.Id,
                userId,
                request.Candidate,
                request.SdpMid,
                request.SdpMLineIndex
            );

            await Clients.Group($"user:{targetUserId}").ReceiveIceCandidate(candidateDto);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error forwarding RTC ICE candidate for session {CallSessionId}", request.CallSessionId);
            throw new HubException(ex.Message);
        }
    }
}
