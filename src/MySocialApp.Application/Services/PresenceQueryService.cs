using Microsoft.EntityFrameworkCore;
using MySocialApp.Application.DTOs.Presence;
using MySocialApp.Application.Interfaces;
using MySocialApp.Domain.Enums;

namespace MySocialApp.Application.Services;

public class PresenceQueryService : IPresenceQueryService
{
    private readonly IApplicationDbContext _context;
    private readonly IUserPresenceService _presenceService;

    public PresenceQueryService(
        IApplicationDbContext context,
        IUserPresenceService presenceService)
    {
        _context = context;
        _presenceService = presenceService;
    }

    public async Task<IReadOnlyList<OnlineFriendDto>> GetOnlineFriendsAsync(
        Guid userId,
        CancellationToken ct = default)
    {
        // 1. Mutual Follow: userId follows other AND other follows userId
        var followingIds = await _context.Follows
            .AsNoTracking()
            .Where(f => f.FollowerId == userId)
            .Select(f => f.FollowingId)
            .ToListAsync(ct);

        HashSet<Guid> mutualFollowIds;
        if (followingIds.Count == 0)
        {
            mutualFollowIds = new HashSet<Guid>();
        }
        else
        {
            var mutualList = await _context.Follows
                .AsNoTracking()
                .Where(f => f.FollowingId == userId && followingIds.Contains(f.FollowerId))
                .Select(f => f.FollowerId)
                .ToListAsync(ct);

            mutualFollowIds = mutualList.ToHashSet();
        }

        // 2. Direct Contact: other users sharing a ConversationType.Direct conversation with userId
        var directConversationIds = await _context.ConversationMembers
            .AsNoTracking()
            .Where(cm => cm.UserId == userId && cm.Conversation.Type == ConversationType.Direct)
            .Select(cm => cm.ConversationId)
            .ToListAsync(ct);

        HashSet<Guid> directContactIds;
        if (directConversationIds.Count == 0)
        {
            directContactIds = new HashSet<Guid>();
        }
        else
        {
            var contactList = await _context.ConversationMembers
                .AsNoTracking()
                .Where(cm => directConversationIds.Contains(cm.ConversationId) && cm.UserId != userId)
                .Select(cm => cm.UserId)
                .Distinct()
                .ToListAsync(ct);

            directContactIds = contactList.ToHashSet();
        }

        var allContactIds = mutualFollowIds
            .Union(directContactIds)
            .Where(id => id != userId)
            .ToList();

        if (allContactIds.Count == 0)
            return Array.Empty<OnlineFriendDto>();

        var users = await _context.Users
            .AsNoTracking()
            .Where(u => allContactIds.Contains(u.Id) && u.IsActive)
            .Select(u => new
            {
                u.Id,
                u.Username,
                u.DisplayName,
                u.AvatarUrl,
                u.LastSeenAt
            })
            .ToListAsync(ct);

        if (users.Count == 0)
            return Array.Empty<OnlineFriendDto>();

        var onlineSet = (await _presenceService.GetOnlineUsersAsync(users.Select(u => u.Id))).ToHashSet();

        return users
            .Select(u => new OnlineFriendDto
            {
                UserId = u.Id,
                Username = u.Username,
                DisplayName = u.DisplayName,
                AvatarUrl = u.AvatarUrl,
                IsOnline = onlineSet.Contains(u.Id),
                LastSeenAt = u.LastSeenAt,
                Relationship = mutualFollowIds.Contains(u.Id) ? "MutualFollow" : "DirectContact"
            })
            .OrderByDescending(x => x.IsOnline)
            .ThenByDescending(x => x.LastSeenAt)
            .ThenBy(x => x.DisplayName, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    public async Task<IReadOnlyList<UserPresenceStatusDto>> GetBatchPresenceAsync(
        IEnumerable<Guid> userIds,
        CancellationToken ct = default)
    {
        if (userIds == null)
            return Array.Empty<UserPresenceStatusDto>();

        var distinctIds = userIds
            .Where(id => id != Guid.Empty)
            .Distinct()
            .Take(200)
            .ToList();

        if (distinctIds.Count == 0)
            return Array.Empty<UserPresenceStatusDto>();

        var users = await _context.Users
            .AsNoTracking()
            .Where(u => distinctIds.Contains(u.Id) && u.IsActive)
            .Select(u => new { u.Id, u.LastSeenAt })
            .ToListAsync(ct);

        if (users.Count == 0)
            return Array.Empty<UserPresenceStatusDto>();

        var onlineSet = (await _presenceService.GetOnlineUsersAsync(users.Select(u => u.Id))).ToHashSet();

        return users
            .Select(u => new UserPresenceStatusDto
            {
                UserId = u.Id,
                IsOnline = onlineSet.Contains(u.Id),
                LastSeenAt = u.LastSeenAt
            })
            .ToList();
    }

    public async Task UpdateLastSeenAsync(Guid userId, CancellationToken ct = default)
    {
        var user = await _context.Users.FirstOrDefaultAsync(u => u.Id == userId, ct);
        if (user == null)
            return;

        user.LastSeenAt = DateTime.UtcNow;
        await _context.SaveChangesAsync(ct);
    }
}
