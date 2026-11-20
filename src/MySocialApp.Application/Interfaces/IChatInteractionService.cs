using MySocialApp.Application.DTOs.Chat;

namespace MySocialApp.Application.Interfaces;

public interface IChatInteractionService
{
    // Reactions
    Task<MessageReactionEventDto> ToggleReactionAsync(
        Guid conversationId,
        Guid messageId,
        Guid userId,
        ToggleReactionRequest request,
        CancellationToken ct = default);

    // Pins
    Task<MessagePinEventDto> PinMessageAsync(Guid conversationId, Guid messageId, Guid userId, CancellationToken ct = default);
    Task<MessagePinEventDto> UnpinMessageAsync(Guid conversationId, Guid messageId, Guid userId, CancellationToken ct = default);
    Task<IReadOnlyList<MessageDto>> GetPinnedMessagesAsync(Guid conversationId, Guid userId, CancellationToken ct = default);

    // Polls
    Task<MessageDto> CreatePollAsync(Guid conversationId, Guid userId, CreatePollRequest request, CancellationToken ct = default);
    Task<PollUpdatedEventDto> VotePollAsync(Guid conversationId, Guid pollId, Guid userId, VotePollRequest request, CancellationToken ct = default);
    Task<PollUpdatedEventDto> ClosePollAsync(Guid conversationId, Guid pollId, Guid userId, CancellationToken ct = default);
    Task<ChatPollDto> GetPollAsync(Guid conversationId, Guid pollId, Guid userId, CancellationToken ct = default);
}
