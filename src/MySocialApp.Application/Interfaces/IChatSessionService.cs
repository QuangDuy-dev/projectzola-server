using MySocialApp.Application.DTOs.Chatbot;

namespace MySocialApp.Application.Interfaces;

public interface IChatSessionService
{
    Task<List<ChatMessage>> GetHistoryAsync(Guid userId);
    Task AppendExchangeAsync(Guid userId, string userMessage, string assistantReply);
    Task ClearSessionAsync(Guid userId);
}
