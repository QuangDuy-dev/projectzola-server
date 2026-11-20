using System.Collections.Concurrent;
using Microsoft.Extensions.Logging;
using MySocialApp.Application.Interfaces;

namespace MySocialApp.Infrastructure.Services;

/// <summary>
/// In-memory user presence tracking service for single-server development.
/// NOTE: This implementation tracks connections in local memory and works for ONE instance.
/// For multi-server or load-balanced production deployment, replace with a distributed Redis/SignalR backplane presence service.
/// </summary>
public class InMemoryUserPresenceService : IUserPresenceService
{
    private readonly ConcurrentDictionary<Guid, HashSet<string>> _userConnections = new();
    private readonly ILogger<InMemoryUserPresenceService> _logger;

    public InMemoryUserPresenceService(ILogger<InMemoryUserPresenceService> logger)
    {
        _logger = logger;
    }

    public Task<bool> UserConnectedAsync(Guid userId, string connectionId)
    {
        bool isNewlyOnline = false;

        _userConnections.AddOrUpdate(
            userId,
            _ =>
            {
                isNewlyOnline = true;
                return new HashSet<string> { connectionId };
            },
            (_, connections) =>
            {
                lock (connections)
                {
                    if (connections.Count == 0)
                    {
                        isNewlyOnline = true;
                    }
                    connections.Add(connectionId);
                }
                return connections;
            });

        _logger.LogInformation("User {UserId} connected from {ConnectionId}. IsNewlyOnline: {IsNewlyOnline}", userId, connectionId, isNewlyOnline);
        return Task.FromResult(isNewlyOnline);
    }

    public Task<bool> UserDisconnectedAsync(Guid userId, string connectionId)
    {
        bool isNewlyOffline = false;

        if (_userConnections.TryGetValue(userId, out var connections))
        {
            lock (connections)
            {
                connections.Remove(connectionId);
                if (connections.Count == 0)
                {
                    isNewlyOffline = true;
                }
            }

            if (isNewlyOffline)
            {
                _userConnections.TryRemove(userId, out _);
            }
        }

        _logger.LogInformation("User {UserId} disconnected from {ConnectionId}. IsNewlyOffline: {IsNewlyOffline}", userId, connectionId, isNewlyOffline);
        return Task.FromResult(isNewlyOffline);
    }

    public Task<bool> IsUserOnlineAsync(Guid userId)
    {
        if (_userConnections.TryGetValue(userId, out var connections))
        {
            lock (connections)
            {
                return Task.FromResult(connections.Count > 0);
            }
        }
        return Task.FromResult(false);
    }

    public Task<int> GetConnectionCountAsync(Guid userId)
    {
        if (_userConnections.TryGetValue(userId, out var connections))
        {
            lock (connections)
            {
                return Task.FromResult(connections.Count);
            }
        }
        return Task.FromResult(0);
    }

    public Task<IReadOnlyCollection<Guid>> GetOnlineUsersAsync(IEnumerable<Guid> userIds)
    {
        var onlineList = new List<Guid>();
        foreach (var id in userIds)
        {
            if (_userConnections.TryGetValue(id, out var connections))
            {
                lock (connections)
                {
                    if (connections.Count > 0)
                    {
                        onlineList.Add(id);
                    }
                }
            }
        }
        return Task.FromResult<IReadOnlyCollection<Guid>>(onlineList);
    }
}
