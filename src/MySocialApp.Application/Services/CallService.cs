using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using MySocialApp.Application.DTOs.Call;
using MySocialApp.Application.DTOs.Chat;
using MySocialApp.Application.Interfaces;
using MySocialApp.Domain.Entities;
using MySocialApp.Domain.Enums;

namespace MySocialApp.Application.Services;

public class CallService : ICallService
{
    private readonly IApplicationDbContext _context;
    private readonly ILogger<CallService> _logger;

    public CallService(
        IApplicationDbContext context,
        ILogger<CallService> logger)
    {
        _context = context;
        _logger = logger;
    }

    public async Task<CallSessionDto> StartCallAsync(Guid callerId, StartCallRequest request)
    {
        var conversation = await _context.Conversations
            .Include(c => c.Members)
            .FirstOrDefaultAsync(c => c.Id == request.ConversationId);

        if (conversation == null)
            throw new KeyNotFoundException("Hội thoại không tồn tại.");

        if (conversation.Type == ConversationType.Group)
            throw new InvalidOperationException("Hiện tại chỉ hỗ trợ cuộc gọi 1-1.");

        var callerMember = conversation.Members.FirstOrDefault(m => m.UserId == callerId);
        var calleeMember = conversation.Members.FirstOrDefault(m => m.UserId == request.CalleeId);

        if (callerMember == null || calleeMember == null)
            throw new UnauthorizedAccessException("Người gọi hoặc người nhận không thuộc hội thoại này.");

        // Check if caller is already in an active call
        var callerActiveCall = await GetActiveCallForUserAsync(callerId);
        if (callerActiveCall != null)
        {
            throw new InvalidOperationException("Bạn đang tham gia một cuộc gọi khác.");
        }

        // Fetch caller and callee user details
        var caller = await _context.Users.FindAsync(callerId);
        var callee = await _context.Users.FindAsync(request.CalleeId);
        if (caller == null || callee == null)
            throw new KeyNotFoundException("Không tìm thấy thông tin người dùng.");

        // Check if callee is already in an active call
        var calleeActiveCall = await GetActiveCallForUserAsync(request.CalleeId);
        var status = calleeActiveCall != null ? CallStatus.Busy : CallStatus.Ringing;

        var session = new CallSession
        {
            ConversationId = request.ConversationId,
            CallerId = callerId,
            CalleeId = request.CalleeId,
            Type = request.Type,
            Status = status,
            RingingAt = status == CallStatus.Ringing ? DateTime.UtcNow : null,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };

        _context.CallSessions.Add(session);
        await _context.SaveChangesAsync();

        if (status == CallStatus.Busy)
        {
            // If busy, immediately record a call log message
            await CreateCallLogMessageAsync(session, CallStatus.Busy, 0, "CalleeBusy");
        }

        return MapToDto(session, caller, callee);
    }

    public async Task<CallSessionDto> AcceptCallAsync(Guid callSessionId, Guid calleeId)
    {
        var session = await _context.CallSessions
            .Include(c => c.Caller)
            .Include(c => c.Callee)
            .FirstOrDefaultAsync(c => c.Id == callSessionId);

        if (session == null)
            throw new KeyNotFoundException("Không tìm thấy phiên cuộc gọi.");

        if (session.CalleeId != calleeId)
            throw new UnauthorizedAccessException("Chỉ người nhận mới có quyền chấp nhận cuộc gọi.");

        if (session.Status != CallStatus.Initiating && session.Status != CallStatus.Ringing)
            throw new InvalidOperationException($"Không thể chấp nhận cuộc gọi đang ở trạng thái {session.Status}.");

        session.Status = CallStatus.Connected;
        session.ConnectedAt = DateTime.UtcNow;
        session.UpdatedAt = DateTime.UtcNow;

        await _context.SaveChangesAsync();

        return MapToDto(session, session.Caller, session.Callee);
    }

    public async Task<(CallEndedDto endedDto, MessageDto? messageDto)> RejectCallAsync(Guid callSessionId, Guid userId)
    {
        var session = await _context.CallSessions
            .Include(c => c.Caller)
            .Include(c => c.Callee)
            .FirstOrDefaultAsync(c => c.Id == callSessionId);

        if (session == null)
            throw new KeyNotFoundException("Không tìm thấy phiên cuộc gọi.");

        if (session.CallerId != userId && session.CalleeId != userId)
            throw new UnauthorizedAccessException("Bạn không thuộc phiên cuộc gọi này.");

        if (session.Status == CallStatus.Ended || session.Status == CallStatus.Rejected || session.Status == CallStatus.Missed)
        {
            return (new CallEndedDto(session.Id, session.ConversationId, session.Status, "AlreadyEnded", session.DurationSeconds, userId), null);
        }

        session.Status = CallStatus.Rejected;
        session.EndedAt = DateTime.UtcNow;
        session.UpdatedAt = DateTime.UtcNow;

        var messageDto = await CreateCallLogMessageAsync(session, CallStatus.Rejected, 0, "RejectedByUser");
        await _context.SaveChangesAsync();

        var endedDto = new CallEndedDto(session.Id, session.ConversationId, session.Status, "Rejected", 0, userId);
        return (endedDto, messageDto);
    }

    public async Task<(CallEndedDto endedDto, MessageDto? messageDto)> EndCallAsync(Guid callSessionId, Guid userId, string reason = "UserHangup")
    {
        var session = await _context.CallSessions
            .Include(c => c.Caller)
            .Include(c => c.Callee)
            .FirstOrDefaultAsync(c => c.Id == callSessionId);

        if (session == null)
            throw new KeyNotFoundException("Không tìm thấy phiên cuộc gọi.");

        if (session.CallerId != userId && session.CalleeId != userId)
            throw new UnauthorizedAccessException("Bạn không thuộc phiên cuộc gọi này.");

        if (session.Status == CallStatus.Ended || session.Status == CallStatus.Rejected || session.Status == CallStatus.Missed)
        {
            return (new CallEndedDto(session.Id, session.ConversationId, session.Status, "AlreadyEnded", session.DurationSeconds, userId), null);
        }

        var now = DateTime.UtcNow;
        int duration = 0;
        if (session.Status == CallStatus.Connected && session.ConnectedAt.HasValue)
        {
            duration = Math.Max(1, (int)(now - session.ConnectedAt.Value).TotalSeconds);
        }

        // If never connected and caller cancelled, status is Cancelled/Missed
        var finalStatus = session.Status == CallStatus.Connected
            ? CallStatus.Ended
            : (userId == session.CallerId ? CallStatus.Missed : CallStatus.Rejected);

        session.Status = finalStatus;
        session.EndedAt = now;
        session.DurationSeconds = duration;
        session.UpdatedAt = now;

        var messageDto = await CreateCallLogMessageAsync(session, finalStatus, duration, reason);
        await _context.SaveChangesAsync();

        var endedDto = new CallEndedDto(session.Id, session.ConversationId, finalStatus, reason, duration, userId);
        return (endedDto, messageDto);
    }

    public async Task<(CallEndedDto endedDto, MessageDto? messageDto)?> HandleTimeoutAsync(Guid callSessionId)
    {
        var session = await _context.CallSessions
            .Include(c => c.Caller)
            .Include(c => c.Callee)
            .FirstOrDefaultAsync(c => c.Id == callSessionId);

        if (session == null || session.Status != CallStatus.Ringing)
            return null;

        session.Status = CallStatus.Missed;
        session.EndedAt = DateTime.UtcNow;
        session.DurationSeconds = 0;
        session.UpdatedAt = DateTime.UtcNow;

        var messageDto = await CreateCallLogMessageAsync(session, CallStatus.Missed, 0, "RingingTimeout");
        await _context.SaveChangesAsync();

        var endedDto = new CallEndedDto(session.Id, session.ConversationId, CallStatus.Missed, "Timeout", 0, session.CallerId);
        return (endedDto, messageDto);
    }

    public async Task<CallSession?> GetActiveCallForUserAsync(Guid userId)
    {
        var cutoff = DateTime.UtcNow.AddMinutes(-30);
        return await _context.CallSessions
            .Where(c => (c.CallerId == userId || c.CalleeId == userId)
                     && (c.Status == CallStatus.Initiating || c.Status == CallStatus.Ringing || c.Status == CallStatus.Connected)
                     && c.CreatedAt >= cutoff)
            .OrderByDescending(c => c.CreatedAt)
            .FirstOrDefaultAsync();
    }

    public async Task<CallSession?> GetCallSessionAsync(Guid callSessionId)
    {
        return await _context.CallSessions
            .Include(c => c.Caller)
            .Include(c => c.Callee)
            .FirstOrDefaultAsync(c => c.Id == callSessionId);
    }

    private async Task<MessageDto?> CreateCallLogMessageAsync(CallSession session, CallStatus status, int duration, string reason)
    {
        try
        {
            var callTypeStr = session.Type == CallType.Video ? "Video" : "Voice";
            var statusStr = status.ToString();
            var durationFormatted = FormatDuration(duration);

            string displayText;
            if (status == CallStatus.Ended)
            {
                displayText = session.Type == CallType.Video
                    ? $"Cuộc gọi video ({durationFormatted})"
                    : $"Cuộc gọi thoại ({durationFormatted})";
            }
            else if (status == CallStatus.Missed)
            {
                displayText = session.Type == CallType.Video
                    ? "Cuộc gọi video nhỡ"
                    : "Cuộc gọi thoại nhỡ";
            }
            else if (status == CallStatus.Rejected)
            {
                displayText = session.Type == CallType.Video
                    ? "Cuộc gọi video bị từ chối"
                    : "Cuộc gọi thoại bị từ chối";
            }
            else
            {
                displayText = session.Type == CallType.Video
                    ? "Cuộc gọi video (máy bận)"
                    : "Cuộc gọi thoại (máy bận)";
            }

            var payload = new
            {
                callSessionId = session.Id,
                callType = callTypeStr,
                status = statusStr,
                durationSeconds = duration,
                durationFormatted,
                callerId = session.CallerId,
                calleeId = session.CalleeId,
                text = displayText,
                reason
            };

            var options = new JsonSerializerOptions
            {
                Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping
            };
            var contentJson = JsonSerializer.Serialize(payload, options);

            var message = new Message
            {
                ConversationId = session.ConversationId,
                SenderId = session.CallerId,
                Type = MessageType.CallLog,
                Content = contentJson,
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow
            };

            _context.Messages.Add(message);

            var conversation = await _context.Conversations.FindAsync(session.ConversationId);
            if (conversation != null)
            {
                conversation.UpdatedAt = DateTime.UtcNow;
            }

            await _context.SaveChangesAsync();
            session.CallLogMessageId = message.Id;

            var fullMessage = await _context.Messages
                .Include(m => m.Sender)
                .FirstOrDefaultAsync(m => m.Id == message.Id);

            return fullMessage != null ? ChatService.MapMessageToDto(fullMessage, session.CallerId) : null;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Lỗi khi tạo tin nhắn CallLog cho session {SessionId}", session.Id);
            return null;
        }
    }

    private static string FormatDuration(int seconds)
    {
        var minutes = seconds / 60;
        var remainingSeconds = seconds % 60;
        return $"{minutes:D2}:{remainingSeconds:D2}";
    }

    private static CallSessionDto MapToDto(CallSession session, User caller, User callee)
    {
        return new CallSessionDto(
            session.Id,
            session.ConversationId,
            session.CallerId,
            caller.DisplayName ?? caller.Username ?? "User",
            caller.AvatarUrl,
            session.CalleeId,
            callee.DisplayName ?? callee.Username ?? "User",
            callee.AvatarUrl,
            session.Type,
            session.Status,
            session.CreatedAt,
            session.ConnectedAt,
            session.EndedAt,
            session.DurationSeconds
        );
    }
}
