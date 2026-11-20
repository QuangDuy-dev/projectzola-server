using MySocialApp.Application.DTOs.Chatbot;

namespace MySocialApp.Application.Interfaces;

public interface IOllamaClient
{
    Task<bool> CheckHealthAsync(CancellationToken cancellationToken = default);
    IAsyncEnumerable<string> StreamChatAsync(List<ChatMessage> messages, CancellationToken cancellationToken = default);
    Task<string> ChatAsync(List<ChatMessage> messages, CancellationToken cancellationToken = default);
}
