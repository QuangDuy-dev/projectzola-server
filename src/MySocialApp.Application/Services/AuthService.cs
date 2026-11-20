using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.IdentityModel.Tokens;
using MySocialApp.Application.DTOs.Auth;
using MySocialApp.Application.Interfaces;
using MySocialApp.Domain.Entities;

namespace MySocialApp.Application.Services;

public class AuthService : IAuthService
{
    private readonly IApplicationDbContext _context;
    private readonly IConfiguration _configuration;
    private readonly IOtpService _otpService;
    private readonly PasswordHasher<User> _passwordHasher;

    public AuthService(
        IApplicationDbContext context,
        IConfiguration configuration,
        IOtpService otpService)
    {
        _context = context;
        _configuration = configuration;
        _otpService = otpService;
        _passwordHasher = new PasswordHasher<User>();
    }

    public async Task<AuthResponse> RegisterAsync(RegisterRequest request, string? deviceInfo = null, string? ipAddress = null)
    {
        var username = request.Username.Trim();
        var normalizedEmail = request.Email.Trim().ToLowerInvariant();

        if (await _context.Users.AnyAsync(u => u.Username == username))
            throw new InvalidOperationException("Username already exists.");

        if (await _context.Users.AnyAsync(u => u.Email.ToLower() == normalizedEmail))
            throw new InvalidOperationException("Email already exists.");

        await _otpService.VerifyAndConsumeOtpAsync(normalizedEmail, request.OtpCode, "Register");

        var user = new User
        {
            Username = username,
            Email = normalizedEmail,
            DisplayName = request.DisplayName.Trim(),
            Role = Domain.Enums.UserRole.User
        };
        user.PasswordHash = _passwordHasher.HashPassword(user, request.Password);

        _context.Users.Add(user);
        await _context.SaveChangesAsync();

        return await CreateSessionAndAuthResponseAsync(user, deviceInfo, ipAddress);
    }

    public async Task<AuthResponse> LoginAsync(LoginRequest request, string? deviceInfo = null, string? ipAddress = null)
    {
        var user = await _context.Users.FirstOrDefaultAsync(u => u.Username == request.Username)
            ?? throw new InvalidOperationException("Invalid username or password.");

        if (!user.IsActive)
            throw new InvalidOperationException("Account is deactivated.");

        var result = _passwordHasher.VerifyHashedPassword(user, user.PasswordHash, request.Password);
        if (result == PasswordVerificationResult.Failed)
            throw new InvalidOperationException("Invalid username or password.");

        return await CreateSessionAndAuthResponseAsync(user, deviceInfo, ipAddress);
    }

    public async Task<AuthResponse> RefreshTokenAsync(string refreshToken, string? deviceInfo = null, string? ipAddress = null)
    {
        if (string.IsNullOrWhiteSpace(refreshToken))
            throw new UnauthorizedAccessException("Refresh token is required.");

        var hash = HashRefreshToken(refreshToken);
        var session = await _context.UserSessions
            .Include(s => s.User)
            .FirstOrDefaultAsync(s => s.RefreshTokenHash == hash);

        if (session == null)
            throw new UnauthorizedAccessException("Invalid refresh token.");

        if (session.RevokedAt.HasValue)
        {
            await RevokeAllUserSessionsAsync(session.UserId);
            await _context.SaveChangesAsync();
            throw new UnauthorizedAccessException("Refresh token has been revoked.");
        }

        if (session.ExpiresAt <= DateTime.UtcNow)
            throw new UnauthorizedAccessException("Refresh token has expired.");

        if (!session.User.IsActive)
            throw new UnauthorizedAccessException("Account is deactivated.");

        var now = DateTime.UtcNow;
        session.RevokedAt = now;
        session.LastUsedAt = now;

        return await CreateSessionAndAuthResponseAsync(
            session.User,
            string.IsNullOrWhiteSpace(deviceInfo) ? session.DeviceInfo : deviceInfo,
            string.IsNullOrWhiteSpace(ipAddress) ? session.IpAddress : ipAddress);
    }

    public async Task LogoutAsync(string? refreshToken)
    {
        if (string.IsNullOrWhiteSpace(refreshToken))
            return;

        var hash = HashRefreshToken(refreshToken);
        var session = await _context.UserSessions.FirstOrDefaultAsync(s => s.RefreshTokenHash == hash);
        if (session != null && !session.RevokedAt.HasValue)
        {
            session.RevokedAt = DateTime.UtcNow;
            await _context.SaveChangesAsync();
        }
    }

    public async Task LogoutAllAsync(Guid userId, Guid? currentSessionId = null, bool keepCurrentSession = false)
    {
        var user = await _context.Users.FindAsync(userId)
            ?? throw new KeyNotFoundException("User not found.");

        var activeSessions = await _context.UserSessions
            .Where(s => s.UserId == userId && s.RevokedAt == null)
            .ToListAsync();

        var now = DateTime.UtcNow;
        foreach (var session in activeSessions)
        {
            if (keepCurrentSession && currentSessionId.HasValue && session.Id == currentSessionId.Value)
                continue;

            session.RevokedAt = now;
        }

        if (!keepCurrentSession)
        {
            user.TokenVersion++;
        }

        await _context.SaveChangesAsync();
    }

    public async Task<IReadOnlyList<UserSessionDto>> GetActiveSessionsAsync(Guid userId, Guid? currentSessionId = null)
    {
        var now = DateTime.UtcNow;
        var sessions = await _context.UserSessions
            .AsNoTracking()
            .Where(s => s.UserId == userId && s.RevokedAt == null && s.ExpiresAt > now)
            .OrderByDescending(s => s.LastUsedAt)
            .Select(s => new UserSessionDto
            {
                Id = s.Id,
                DeviceInfo = s.DeviceInfo,
                IpAddress = s.IpAddress,
                CreatedAt = s.CreatedAt,
                LastUsedAt = s.LastUsedAt,
                ExpiresAt = s.ExpiresAt,
                IsCurrent = currentSessionId.HasValue && s.Id == currentSessionId.Value
            })
            .ToListAsync();

        return sessions;
    }

    public async Task RevokeSessionAsync(Guid userId, Guid sessionId)
    {
        var session = await _context.UserSessions.FirstOrDefaultAsync(s => s.Id == sessionId)
            ?? throw new KeyNotFoundException("Session not found.");

        if (session.UserId != userId)
            throw new UnauthorizedAccessException("You do not have permission to revoke this session.");

        if (!session.RevokedAt.HasValue)
        {
            session.RevokedAt = DateTime.UtcNow;
            await _context.SaveChangesAsync();
        }
    }

    public async Task<UserInfoDto> GetCurrentUserAsync(Guid userId)
    {
        var user = await _context.Users.FindAsync(userId)
            ?? throw new KeyNotFoundException("User not found.");

        return new UserInfoDto
        {
            Id = user.Id,
            Username = user.Username,
            Email = user.Email,
            DisplayName = user.DisplayName,
            AvatarUrl = user.AvatarUrl,
            Bio = user.Bio,
            Role = user.Role.ToString()
        };
    }

    public async Task ForgotPasswordResetAsync(ForgotPasswordResetRequest request)
    {
        var normalizedEmail = request.Email.Trim().ToLowerInvariant();
        var user = await _context.Users.FirstOrDefaultAsync(u => u.Email.ToLower() == normalizedEmail)
            ?? throw new KeyNotFoundException("No account found with this email.");

        await _otpService.VerifyAndConsumeOtpAsync(normalizedEmail, request.OtpCode, "ForgotPassword");

        user.PasswordHash = _passwordHasher.HashPassword(user, request.NewPassword);
        user.TokenVersion++;
        await RevokeAllUserSessionsAsync(user.Id);
        await _context.SaveChangesAsync();
    }

    public async Task ChangePasswordAsync(Guid userId, ChangePasswordRequest request)
    {
        var user = await _context.Users.FindAsync(userId)
            ?? throw new KeyNotFoundException("User not found.");

        var verifyResult = _passwordHasher.VerifyHashedPassword(user, user.PasswordHash, request.CurrentPassword);
        if (verifyResult == PasswordVerificationResult.Failed)
        {
            throw new InvalidOperationException("Current password is incorrect.");
        }

        user.PasswordHash = _passwordHasher.HashPassword(user, request.NewPassword);
        user.TokenVersion++;
        await RevokeAllUserSessionsAsync(user.Id);
        await _context.SaveChangesAsync();
    }

    private async Task RevokeAllUserSessionsAsync(Guid userId)
    {
        var activeSessions = await _context.UserSessions
            .Where(s => s.UserId == userId && s.RevokedAt == null)
            .ToListAsync();

        var now = DateTime.UtcNow;
        foreach (var session in activeSessions)
        {
            session.RevokedAt = now;
        }
    }

    private async Task<AuthResponse> CreateSessionAndAuthResponseAsync(User user, string? deviceInfo, string? ipAddress)
    {
        var refreshToken = GenerateSecureRefreshToken();
        var refreshTokenHash = HashRefreshToken(refreshToken);
        var refreshDays = double.TryParse(_configuration["Jwt:RefreshTokenExpireDays"], out var parsedDays) && parsedDays > 0
            ? parsedDays
            : 7d;
        var now = DateTime.UtcNow;
        var refreshTokenExpiration = now.AddDays(refreshDays);

        var session = new UserSession
        {
            UserId = user.Id,
            RefreshTokenHash = refreshTokenHash,
            DeviceInfo = NormalizeDeviceInfo(deviceInfo),
            IpAddress = NormalizeIpAddress(ipAddress),
            ExpiresAt = refreshTokenExpiration,
            CreatedAt = now,
            LastUsedAt = now
        };

        _context.UserSessions.Add(session);
        await _context.SaveChangesAsync();

        var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(
            _configuration["Jwt:Key"] ?? throw new InvalidOperationException("JWT Key not configured")));
        var credentials = new SigningCredentials(key, SecurityAlgorithms.HmacSha256);
        var expiration = GetAccessTokenExpiration(now);

        var claims = new[]
        {
            new Claim(ClaimTypes.NameIdentifier, user.Id.ToString()),
            new Claim(ClaimTypes.Name, user.Username),
            new Claim(ClaimTypes.Role, user.Role.ToString()),
            new Claim("token_version", user.TokenVersion.ToString()),
            new Claim("session_id", session.Id.ToString())
        };

        var token = new JwtSecurityToken(
            issuer: _configuration["Jwt:Issuer"],
            audience: _configuration["Jwt:Audience"],
            claims: claims,
            expires: expiration,
            signingCredentials: credentials
        );

        return new AuthResponse
        {
            AccessToken = new JwtSecurityTokenHandler().WriteToken(token),
            Expiration = expiration,
            RefreshToken = refreshToken,
            RefreshTokenExpiration = refreshTokenExpiration,
            SessionId = session.Id,
            User = new UserInfoDto
            {
                Id = user.Id,
                Username = user.Username,
                Email = user.Email,
                DisplayName = user.DisplayName,
                AvatarUrl = user.AvatarUrl,
                Bio = user.Bio,
                Role = user.Role.ToString()
            }
        };
    }

    private DateTime GetAccessTokenExpiration(DateTime utcNow)
    {
        if (double.TryParse(_configuration["Jwt:ExpireMinutes"], out var expireMinutes) && expireMinutes > 0)
        {
            return utcNow.AddMinutes(expireMinutes);
        }

        if (double.TryParse(_configuration["Jwt:ExpireHours"], out var expireHours) && expireHours > 0)
        {
            return utcNow.AddHours(expireHours);
        }

        return utcNow.AddMinutes(30);
    }

    private static string GenerateSecureRefreshToken()
    {
        var bytes = RandomNumberGenerator.GetBytes(64);
        return Convert.ToBase64String(bytes)
            .TrimEnd('=')
            .Replace('+', '-')
            .Replace('/', '_');
    }

    private static string HashRefreshToken(string refreshToken)
    {
        var hashBytes = SHA256.HashData(Encoding.UTF8.GetBytes(refreshToken));
        return Convert.ToHexString(hashBytes).ToLowerInvariant();
    }

    private static string NormalizeDeviceInfo(string? deviceInfo)
    {
        if (string.IsNullOrWhiteSpace(deviceInfo))
            return "Unknown Device";

        var trimmed = deviceInfo.Trim();
        return trimmed.Length <= 256 ? trimmed : trimmed[..256];
    }

    private static string? NormalizeIpAddress(string? ipAddress)
    {
        if (string.IsNullOrWhiteSpace(ipAddress))
            return null;

        var trimmed = ipAddress.Trim();
        return trimmed.Length <= 64 ? trimmed : trimmed[..64];
    }
}
