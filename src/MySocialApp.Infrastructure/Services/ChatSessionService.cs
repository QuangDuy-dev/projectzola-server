using System.Collections.Concurrent;
using Microsoft.Extensions.Caching.Memory;
using MySocialApp.Application.DTOs.Chatbot;
using MySocialApp.Application.Interfaces;

namespace MySocialApp.Infrastructure.Services;

public class ChatSessionService : IChatSessionService
{
    private readonly IMemoryCache _cache;
    private static readonly TimeSpan SlidingExpiry = TimeSpan.FromMinutes(5);
    private const int MaxHistoryMessages = 8; // 4 turns (user + assistant)
    private static readonly ConcurrentDictionary<Guid, SemaphoreSlim> UserLocks = new();

    public ChatSessionService(IMemoryCache cache)
    {
        _cache = cache;
    }

    private static string GetCacheKey(Guid userId) => $"ChatbotSession:{userId}";

    private static SemaphoreSlim GetUserLock(Guid userId) =>
        UserLocks.GetOrAdd(userId, _ => new SemaphoreSlim(1, 1));

    public async Task<List<ChatMessage>> GetHistoryAsync(Guid userId)
    {
        var userLock = GetUserLock(userId);
        await userLock.WaitAsync();
        try
        {
            if (_cache.TryGetValue(GetCacheKey(userId), out List<ChatMessage>? history) && history != null)
            {
                // Touch cache sliding expiration
                _cache.Set(GetCacheKey(userId), history, new MemoryCacheEntryOptions
                {
                    SlidingExpiration = SlidingExpiry
                });

                return history.Select(m => new ChatMessage(m.Role, m.Content)).ToList();
            }

            return new List<ChatMessage>();
        }
        finally
        {
            userLock.Release();
        }
    }

    public async Task AppendExchangeAsync(Guid userId, string userMessage, string assistantReply)
    {
        if (string.IsNullOrWhiteSpace(userMessage) || string.IsNullOrWhiteSpace(assistantReply))
            return;

        var userLock = GetUserLock(userId);
        await userLock.WaitAsync();
        try
        {
            if (!_cache.TryGetValue(GetCacheKey(userId), out List<ChatMessage>? history) || history == null)
            {
                history = new List<ChatMessage>();
            }

            history.Add(new ChatMessage("user", userMessage.Trim()));
            history.Add(new ChatMessage("assistant", assistantReply.Trim()));

            // Keep only the most recent bounded messages (FIFO eviction)
            while (history.Count > MaxHistoryMessages)
            {
                history.RemoveAt(0);
            }

            _cache.Set(GetCacheKey(userId), history, new MemoryCacheEntryOptions
            {
                SlidingExpiration = SlidingExpiry
            });
        }
        finally
        {
            userLock.Release();
        }
    }

    public async Task ClearSessionAsync(Guid userId)
    {
        var userLock = GetUserLock(userId);
        await userLock.WaitAsync();
        try
        {
            _cache.Remove(GetCacheKey(userId));
        }
        finally
        {
            userLock.Release();
        }
    }
}
