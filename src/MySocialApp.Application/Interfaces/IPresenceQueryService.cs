using MySocialApp.Application.DTOs.Presence;

namespace MySocialApp.Application.Interfaces;

public interface IPresenceQueryService
{
    Task<IReadOnlyList<OnlineFriendDto>> GetOnlineFriendsAsync(Guid userId, CancellationToken ct = default);
    Task<IReadOnlyList<UserPresenceStatusDto>> GetBatchPresenceAsync(IEnumerable<Guid> userIds, CancellationToken ct = default);
    Task UpdateLastSeenAsync(Guid userId, CancellationToken ct = default);
}
