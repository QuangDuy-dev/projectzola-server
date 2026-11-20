using MySocialApp.Domain.Common;

namespace MySocialApp.Domain.Entities;

public class UserSession : BaseEntity
{
    public Guid UserId { get; set; }
    public string RefreshTokenHash { get; set; } = string.Empty;
    public string DeviceInfo { get; set; } = string.Empty;
    public string? IpAddress { get; set; }
    public DateTime ExpiresAt { get; set; }
    public DateTime LastUsedAt { get; set; } = DateTime.UtcNow;
    public DateTime? RevokedAt { get; set; }

    public bool IsRevoked => RevokedAt.HasValue;

    public User User { get; set; } = null!;
}
