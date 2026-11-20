namespace MySocialApp.Application.Interfaces;

public interface IUserPresenceService
{
    Task<bool> UserConnectedAsync(Guid userId, string connectionId);
    Task<bool> UserDisconnectedAsync(Guid userId, string connectionId);
    Task<bool> IsUserOnlineAsync(Guid userId);
    Task<int> GetConnectionCountAsync(Guid userId);
    Task<IReadOnlyCollection<Guid>> GetOnlineUsersAsync(IEnumerable<Guid> userIds);
}
