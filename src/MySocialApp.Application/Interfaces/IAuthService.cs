using MySocialApp.Application.DTOs.Auth;

namespace MySocialApp.Application.Interfaces;

public interface IAuthService
{
    Task<AuthResponse> RegisterAsync(RegisterRequest request, string? deviceInfo = null, string? ipAddress = null);
    Task<AuthResponse> LoginAsync(LoginRequest request, string? deviceInfo = null, string? ipAddress = null);
    Task<AuthResponse> RefreshTokenAsync(string refreshToken, string? deviceInfo = null, string? ipAddress = null);
    Task LogoutAsync(string? refreshToken);
    Task LogoutAllAsync(Guid userId, Guid? currentSessionId = null, bool keepCurrentSession = false);
    Task<IReadOnlyList<UserSessionDto>> GetActiveSessionsAsync(Guid userId, Guid? currentSessionId = null);
    Task RevokeSessionAsync(Guid userId, Guid sessionId);
    Task<UserInfoDto> GetCurrentUserAsync(Guid userId);
    Task ForgotPasswordResetAsync(ForgotPasswordResetRequest request);
    Task ChangePasswordAsync(Guid userId, ChangePasswordRequest request);
}
