using MySocialApp.Application.DTOs.Chat;

namespace MySocialApp.Application.Interfaces;

public interface IChatMediaService
{
    Task<MessageDto> SendMediaMessageAsync(
        Guid conversationId,
        Guid senderId,
        IReadOnlyList<SendMediaMessageItem> files,
        string? caption = null,
        Guid? replyToMessageId = null,
        List<Guid>? mentionedUserIds = null,
        bool mentionAll = false,
        string? clientMessageId = null,
        CancellationToken ct = default);

    Task<MessageDto> SendVoiceMessageAsync(
        Guid conversationId,
        Guid senderId,
        SendVoiceMessageInput input,
        CancellationToken ct = default);
}
