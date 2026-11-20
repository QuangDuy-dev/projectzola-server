using System.IdentityModel.Tokens.Jwt;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using MySocialApp.Application.DTOs.Auth;
using MySocialApp.Application.Interfaces;
using MySocialApp.Application.Services;
using MySocialApp.Infrastructure.Services;

namespace MySocialApp.Tests;

public class AuthServiceTests
{
    private readonly IConfiguration _configuration;

    public AuthServiceTests()
    {
        var inMemorySettings = new Dictionary<string, string?>
        {
            {"Jwt:Key", "TestSuperSecretKeyThatIsAtLeast32CharsLong!"},
            {"Jwt:Issuer", "MySocialAppTest"},
            {"Jwt:Audience", "MySocialAppTest"},
            {"Jwt:ExpireHours", "24"}
        };

        _configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(inMemorySettings)
            .Build();
    }

    private sealed class FakeEmailService : IEmailService
    {
        public Task<bool> SendEmailAsync(string toEmail, string subject, string htmlBody, string? plainTextForLog = null)
            => Task.FromResult(false);
    }

    private (AuthService authService, OtpService otpService) CreateServices(IApplicationDbContext context)
    {
        var otpService = new OtpService(context, new FakeEmailService(), NullLogger<OtpService>.Instance);
        var authService = new AuthService(context, _configuration, otpService);
        return (authService, otpService);
    }

    [Fact]
    public async Task Register_ValidRequest_CreatesUserAndReturnsToken()
    {
        using var context = TestDbContextFactory.Create(nameof(Register_ValidRequest_CreatesUserAndReturnsToken));
        var (authService, otpService) = CreateServices(context);

        var otpResponse = await otpService.SendOtpAsync("john@example.com", "Register");
        Assert.False(string.IsNullOrWhiteSpace(otpResponse.DevOtpHint));

        var request = new RegisterRequest
        {
            Username = "johndoe",
            Email = "john@example.com",
            Password = "Password123!",
            DisplayName = "John Doe",
            OtpCode = otpResponse.DevOtpHint!
        };

        var response = await authService.RegisterAsync(request);

        Assert.NotNull(response);
        Assert.False(string.IsNullOrWhiteSpace(response.AccessToken));
        Assert.Equal("johndoe", response.User.Username);
        Assert.Equal("john@example.com", response.User.Email);
        Assert.Equal("User", response.User.Role);
    }

    [Fact]
    public async Task Register_DuplicateUsername_ThrowsInvalidOperationException()
    {
        using var context = TestDbContextFactory.Create(nameof(Register_DuplicateUsername_ThrowsInvalidOperationException));
        var (authService, otpService) = CreateServices(context);

        var otp1 = await otpService.SendOtpAsync("user1@example.com", "Register");
        var request = new RegisterRequest
        {
            Username = "duplicate",
            Email = "user1@example.com",
            Password = "Password123!",
            DisplayName = "User One",
            OtpCode = otp1.DevOtpHint!
        };

        await authService.RegisterAsync(request);

        var otp2 = await otpService.SendOtpAsync("user2@example.com", "Register");
        var duplicateRequest = new RegisterRequest
        {
            Username = "duplicate",
            Email = "user2@example.com",
            Password = "Password123!",
            DisplayName = "User Two",
            OtpCode = otp2.DevOtpHint!
        };

        await Assert.ThrowsAsync<InvalidOperationException>(() => authService.RegisterAsync(duplicateRequest));
    }

    [Fact]
    public async Task Login_ValidCredentials_ReturnsToken()
    {
        using var context = TestDbContextFactory.Create(nameof(Login_ValidCredentials_ReturnsToken));
        var (authService, otpService) = CreateServices(context);

        var otp = await otpService.SendOtpAsync("login@example.com", "Register");
        await authService.RegisterAsync(new RegisterRequest
        {
            Username = "loginuser",
            Email = "login@example.com",
            Password = "SecretPassword123",
            DisplayName = "Login User",
            OtpCode = otp.DevOtpHint!
        });

        var loginResponse = await authService.LoginAsync(new LoginRequest
        {
            Username = "loginuser",
            Password = "SecretPassword123"
        });

        Assert.NotNull(loginResponse);
        Assert.False(string.IsNullOrWhiteSpace(loginResponse.AccessToken));
        Assert.Equal("loginuser", loginResponse.User.Username);
    }

    [Fact]
    public async Task Login_WrongPassword_ThrowsInvalidOperationException()
    {
        using var context = TestDbContextFactory.Create(nameof(Login_WrongPassword_ThrowsInvalidOperationException));
        var (authService, otpService) = CreateServices(context);

        var otp = await otpService.SendOtpAsync("wrong@example.com", "Register");
        await authService.RegisterAsync(new RegisterRequest
        {
            Username = "wrongpwduser",
            Email = "wrong@example.com",
            Password = "CorrectPassword123",
            DisplayName = "User",
            OtpCode = otp.DevOtpHint!
        });

        await Assert.ThrowsAsync<InvalidOperationException>(() => authService.LoginAsync(new LoginRequest
        {
            Username = "wrongpwduser",
            Password = "WrongPassword!"
        }));
    }

    [Fact]
    public async Task ForgotPasswordReset_And_ChangePassword_WorkEndToEnd()
    {
        using var context = TestDbContextFactory.Create(nameof(ForgotPasswordReset_And_ChangePassword_WorkEndToEnd));
        var (authService, otpService) = CreateServices(context);

        var regOtp = await otpService.SendOtpAsync("reset@example.com", "Register");
        var registered = await authService.RegisterAsync(new RegisterRequest
        {
            Username = "resetuser",
            Email = "reset@example.com",
            Password = "InitialPassword123",
            DisplayName = "Reset User",
            OtpCode = regOtp.DevOtpHint!
        });

        var forgotOtp = await otpService.SendOtpAsync("reset@example.com", "ForgotPassword");
        Assert.True(await otpService.VerifyOtpAsync("reset@example.com", forgotOtp.DevOtpHint!, "ForgotPassword"));

        await authService.ForgotPasswordResetAsync(new ForgotPasswordResetRequest
        {
            Email = "reset@example.com",
            OtpCode = forgotOtp.DevOtpHint!,
            NewPassword = "NewPassword456"
        });

        var loginAfterReset = await authService.LoginAsync(new LoginRequest
        {
            Username = "resetuser",
            Password = "NewPassword456"
        });
        Assert.Equal("resetuser", loginAfterReset.User.Username);

        await authService.ChangePasswordAsync(registered.User.Id, new ChangePasswordRequest
        {
            CurrentPassword = "NewPassword456",
            NewPassword = "ChangedPassword789"
        });

        var loginAfterChange = await authService.LoginAsync(new LoginRequest
        {
            Username = "resetuser",
            Password = "ChangedPassword789"
        });
        Assert.Equal("resetuser", loginAfterChange.User.Username);
    }

    [Fact]
    public async Task Login_CreatesUserSessionWithHashedRefreshToken_AndJwtContainsTokenVersion()
    {
        using var context = TestDbContextFactory.Create(nameof(Login_CreatesUserSessionWithHashedRefreshToken_AndJwtContainsTokenVersion));
        var (authService, otpService) = CreateServices(context);

        var otp = await otpService.SendOtpAsync("session@example.com", "Register");
        await authService.RegisterAsync(new RegisterRequest
        {
            Username = "sessionuser",
            Email = "session@example.com",
            Password = "Password123!",
            DisplayName = "Session User",
            OtpCode = otp.DevOtpHint!
        });

        var loginResponse = await authService.LoginAsync(
            new LoginRequest { Username = "sessionuser", Password = "Password123!" },
            deviceInfo: "Chrome on Windows",
            ipAddress: "127.0.0.1");

        Assert.False(string.IsNullOrWhiteSpace(loginResponse.RefreshToken));
        Assert.NotEqual(Guid.Empty, loginResponse.SessionId);

        var session = await context.UserSessions.FirstOrDefaultAsync(s => s.Id == loginResponse.SessionId);
        Assert.NotNull(session);
        Assert.NotEqual(loginResponse.RefreshToken, session.RefreshTokenHash);
        Assert.Equal(64, session.RefreshTokenHash.Length);
        Assert.Equal("Chrome on Windows", session.DeviceInfo);
        Assert.Equal("127.0.0.1", session.IpAddress);
        Assert.Null(session.RevokedAt);

        var jwt = new JwtSecurityTokenHandler().ReadJwtToken(loginResponse.AccessToken);
        Assert.Equal("1", jwt.Claims.First(c => c.Type == "token_version").Value);
        Assert.Equal(loginResponse.SessionId.ToString(), jwt.Claims.First(c => c.Type == "session_id").Value);
    }

    [Fact]
    public async Task RefreshToken_RotatesToken_AndRejectsReusedOldToken()
    {
        using var context = TestDbContextFactory.Create(nameof(RefreshToken_RotatesToken_AndRejectsReusedOldToken));
        var (authService, otpService) = CreateServices(context);

        var otp = await otpService.SendOtpAsync("refresh@example.com", "Register");
        var reg = await authService.RegisterAsync(new RegisterRequest
        {
            Username = "refreshuser",
            Email = "refresh@example.com",
            Password = "Password123!",
            DisplayName = "Refresh User",
            OtpCode = otp.DevOtpHint!
        });

        var rotated = await authService.RefreshTokenAsync(reg.RefreshToken);
        Assert.NotEqual(reg.RefreshToken, rotated.RefreshToken);
        Assert.NotEqual(reg.SessionId, rotated.SessionId);

        var oldSession = await context.UserSessions.FindAsync(reg.SessionId);
        Assert.NotNull(oldSession?.RevokedAt);

        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => authService.RefreshTokenAsync(reg.RefreshToken));

        var newSessionAfterReuse = await context.UserSessions.FindAsync(rotated.SessionId);
        Assert.NotNull(newSessionAfterReuse?.RevokedAt);
    }

    [Fact]
    public async Task ChangePassword_IncrementsTokenVersion_AndRevokesAllSessions()
    {
        using var context = TestDbContextFactory.Create(nameof(ChangePassword_IncrementsTokenVersion_AndRevokesAllSessions));
        var (authService, otpService) = CreateServices(context);

        var otp = await otpService.SendOtpAsync("pw@example.com", "Register");
        var reg = await authService.RegisterAsync(new RegisterRequest
        {
            Username = "pwuser",
            Email = "pw@example.com",
            Password = "Password123!",
            DisplayName = "PW User",
            OtpCode = otp.DevOtpHint!
        });

        await authService.LoginAsync(new LoginRequest { Username = "pwuser", Password = "Password123!" });

        await authService.ChangePasswordAsync(reg.User.Id, new ChangePasswordRequest
        {
            CurrentPassword = "Password123!",
            NewPassword = "NewPassword456!"
        });

        var user = await context.Users.FindAsync(reg.User.Id);
        Assert.Equal(2, user!.TokenVersion);
        Assert.All(context.UserSessions.Where(s => s.UserId == reg.User.Id), s => Assert.NotNull(s.RevokedAt));
    }

    [Fact]
    public async Task AdminDeactivate_IncrementsTokenVersion_AndRevokesAllSessions()
    {
        using var context = TestDbContextFactory.Create(nameof(AdminDeactivate_IncrementsTokenVersion_AndRevokesAllSessions));
        var (authService, otpService) = CreateServices(context);
        var adminService = new AdminService(context);

        var otp = await otpService.SendOtpAsync("lock@example.com", "Register");
        var reg = await authService.RegisterAsync(new RegisterRequest
        {
            Username = "lockuser",
            Email = "lock@example.com",
            Password = "Password123!",
            DisplayName = "Lock User",
            OtpCode = otp.DevOtpHint!
        });

        await adminService.ToggleUserActiveAsync(reg.User.Id);

        var user = await context.Users.FindAsync(reg.User.Id);
        Assert.False(user!.IsActive);
        Assert.Equal(2, user.TokenVersion);
        Assert.All(context.UserSessions.Where(s => s.UserId == reg.User.Id), s => Assert.NotNull(s.RevokedAt));
    }

    [Fact]
    public async Task SessionsManagement_ListRevokeAndLogoutAll_WorkCorrectly()
    {
        using var context = TestDbContextFactory.Create(nameof(SessionsManagement_ListRevokeAndLogoutAll_WorkCorrectly));
        var (authService, otpService) = CreateServices(context);

        var otp = await otpService.SendOtpAsync("multi@example.com", "Register");
        var s1 = await authService.RegisterAsync(new RegisterRequest
        {
            Username = "multiuser",
            Email = "multi@example.com",
            Password = "Password123!",
            DisplayName = "Multi User",
            OtpCode = otp.DevOtpHint!
        }, deviceInfo: "Device 1");

        var s2 = await authService.LoginAsync(
            new LoginRequest { Username = "multiuser", Password = "Password123!" },
            deviceInfo: "Device 2");

        var sessions = await authService.GetActiveSessionsAsync(s1.User.Id, s1.SessionId);
        Assert.Equal(2, sessions.Count);
        Assert.Single(sessions, s => s.IsCurrent && s.Id == s1.SessionId);

        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => authService.RevokeSessionAsync(Guid.NewGuid(), s2.SessionId));

        await authService.RevokeSessionAsync(s1.User.Id, s2.SessionId);
        sessions = await authService.GetActiveSessionsAsync(s1.User.Id, s1.SessionId);
        Assert.Single(sessions);

        await authService.LogoutAllAsync(s1.User.Id, s1.SessionId, keepCurrentSession: true);
        Assert.Single(await authService.GetActiveSessionsAsync(s1.User.Id, s1.SessionId));

        await authService.LogoutAllAsync(s1.User.Id, s1.SessionId, keepCurrentSession: false);
        Assert.Empty(await authService.GetActiveSessionsAsync(s1.User.Id, s1.SessionId));
        var user = await context.Users.FindAsync(s1.User.Id);
        Assert.Equal(2, user!.TokenVersion);
    }
}
