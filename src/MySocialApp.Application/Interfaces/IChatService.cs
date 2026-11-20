using MySocialApp.Application.DTOs.Chat;
using MySocialApp.Application.DTOs.Common;

namespace MySocialApp.Application.Interfaces;

public interface IChatService
{
    Task<PagedResult<ConversationDto>> GetConversationsAsync(Guid userId, PaginationParams pagination);
    Task<ConversationDto> GetOrCreateConversationAsync(Guid userId, Guid participantId);
    Task<PagedResult<MessageDto>> GetMessagesAsync(Guid conversationId, Guid userId, PaginationParams pagination);
    Task<MessageDto> SendMessageAsync(Guid conversationId, Guid senderId, SendMessageRequest request);
    Task<MessageDto> EditMessageAsync(Guid messageId, Guid userId, EditMessageRequest request);
    Task<MessageDeletedDto> DeleteMessageAsync(Guid messageId, Guid userId);
    Task<MessageReadDto> MarkAsReadAsync(Guid conversationId, Guid userId, MarkReadRequest request);
    Task<IReadOnlyList<Guid>> GetUserConversationIdsAsync(Guid userId);
    Task<bool> CanMessageUserAsync(Guid senderId, Guid recipientId);
    Task<bool> IsMemberAsync(Guid conversationId, Guid userId);
}
