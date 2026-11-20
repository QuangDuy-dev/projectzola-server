using MySocialApp.Application.DTOs.Chatbot;

namespace MySocialApp.Application.Interfaces;

public interface IChatbotService
{
    IAsyncEnumerable<ChatbotChunk> StreamChatAsync(Guid userId, string userMessage, CancellationToken cancellationToken = default);
    Task<ChatbotResponse> ChatAsync(Guid userId, string userMessage, CancellationToken cancellationToken = default);
    Task ClearSessionAsync(Guid userId);
    Task<bool> CheckHealthAsync(CancellationToken cancellationToken = default);
}
