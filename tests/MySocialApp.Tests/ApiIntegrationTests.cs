using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using MySocialApp.Application.DTOs.Auth;
using MySocialApp.Application.DTOs.Common;
using MySocialApp.Domain.Entities;
using MySocialApp.Infrastructure.Data;

namespace MySocialApp.Tests;

public class ApiIntegrationTests : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly WebApplicationFactory<Program> _factory;
    private readonly HttpClient _client;

    public ApiIntegrationTests(WebApplicationFactory<Program> factory)
    {
        _factory = factory;
        _client = factory.CreateClient();
    }

    [Fact]
    public async Task ConcurrentAccounts_GetMeKeepsIdentityAndDisablesSharedCaching()
    {
        async Task<AuthResponse> Login(string username, string password)
        {
            using var response = await _client.PostAsJsonAsync("/api/auth/login", new LoginRequest { Username = username, Password = password });
            response.EnsureSuccessStatusCode();
            var body = await response.Content.ReadFromJsonAsync<ApiResponse<AuthResponse>>();
            return body!.Data!;
        }

        var accountA = await Login("admin", "Admin@123");
        var accountB = await Login("user1", "User@123");
        await Task.WhenAll(Enumerable.Range(0, 12).Select(async index =>
        {
            var account = index % 2 == 0 ? accountA : accountB;
            using var request = new HttpRequestMessage(HttpMethod.Get, "/api/auth/me");
            request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", account.AccessToken);
            using var response = await _client.SendAsync(request);
            response.EnsureSuccessStatusCode();
            var body = await response.Content.ReadFromJsonAsync<ApiResponse<UserInfoDto>>();
            Assert.Equal(account.User.Id, body!.Data!.Id);
            Assert.True(response.Headers.CacheControl?.NoStore);
            Assert.True(response.Headers.CacheControl?.Private);
            Assert.Contains("Authorization", response.Headers.Vary);
        }));
    }

    [Fact]
    public async Task SwaggerEndpoint_ReturnsSuccessAndJson()
    {
        var response = await _client.GetAsync("/swagger/v1/swagger.json");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var content = await response.Content.ReadAsStringAsync();
        Assert.Contains("MySocialApp API", content);
    }

    [Fact]
    public async Task Login_WithSeedAdmin_ReturnsSuccessAndJwtToken()
    {
        var loginRequest = new LoginRequest
        {
            Username = "admin",
            Password = "Admin@123"
        };

        var response = await _client.PostAsJsonAsync("/api/auth/login", loginRequest);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var body = await response.Content.ReadFromJsonAsync<ApiResponse<AuthResponse>>();
        Assert.NotNull(body);
        Assert.True(body.Success);
        Assert.NotNull(body.Data);
        Assert.False(string.IsNullOrWhiteSpace(body.Data.AccessToken));
        Assert.Equal("admin", body.Data.User.Username);
        Assert.Equal("Admin", body.Data.User.Role);
    }

    [Fact]
    public async Task Register_NewUser_ReturnsCreated()
    {
        var username = $"user_{Guid.NewGuid():N}"[..12];
        var email = $"{username}@test.com";

        var otpResponse = await _client.PostAsJsonAsync("/api/auth/send-otp", new SendOtpRequest
        {
            Email = email,
            Purpose = "Register"
        });
        Assert.Equal(HttpStatusCode.OK, otpResponse.StatusCode);

        var otpBody = await otpResponse.Content.ReadFromJsonAsync<ApiResponse<SendOtpResponse>>();
        Assert.NotNull(otpBody?.Data?.DevOtpHint);

        var registerRequest = new RegisterRequest
        {
            Username = username,
            Email = email,
            Password = "Password123!",
            DisplayName = "Integration Test User",
            OtpCode = otpBody.Data.DevOtpHint
        };

        var response = await _client.PostAsJsonAsync("/api/auth/register", registerRequest);
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);

        var body = await response.Content.ReadFromJsonAsync<ApiResponse<AuthResponse>>();
        Assert.NotNull(body);
        Assert.True(body.Success);
        Assert.NotNull(body.Data);
        Assert.Equal(username, body.Data.User.Username);
    }

    [Fact]
    public async Task AdminEndpoint_RejectsNormalUser_ReturnsForbidden()
    {
        // Login as normal user
        var loginResponse = await _client.PostAsJsonAsync("/api/auth/login", new LoginRequest
        {
            Username = "user1",
            Password = "User@123"
        });
        var loginBody = await loginResponse.Content.ReadFromJsonAsync<ApiResponse<AuthResponse>>();
        Assert.NotNull(loginBody?.Data?.AccessToken);

        using var request = new HttpRequestMessage(HttpMethod.Get, "/api/admin/users");
        request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", loginBody.Data.AccessToken);

        var response = await _client.SendAsync(request);
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task SwaggerEndpoint_IncludesMediaStorageEndpoints()
    {
        var response = await _client.GetAsync("/swagger/v1/swagger.json");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var content = await response.Content.ReadAsStringAsync();

        Assert.Contains("/api/users/me/avatar", content);
        Assert.Contains("/api/posts/{id}/media", content);
        Assert.Contains("/api/products/{id}/images", content);
        Assert.Contains("/api/notifications", content);
    }

    [Fact]
    public async Task StaticFiles_MediaRoute_IsMounted()
    {
        var response = await _client.GetAsync("/media/non_existent_test_file.png");
        // Handled by static files middleware (404 not found file, not 500 error)
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Login_SetsHttpOnlyRefreshCookie_And_RefreshRotatesViaCookie()
    {
        using var client = _factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            HandleCookies = false
        });

        var loginResponse = await client.PostAsJsonAsync("/api/auth/login", new LoginRequest
        {
            Username = "user1",
            Password = "User@123"
        });
        Assert.Equal(HttpStatusCode.OK, loginResponse.StatusCode);

        var setCookieHeaders = loginResponse.Headers.GetValues("Set-Cookie").ToList();
        var refreshCookieHeader = Assert.Single(setCookieHeaders, h => h.StartsWith("refreshToken=", StringComparison.OrdinalIgnoreCase));
        Assert.Contains("httponly", refreshCookieHeader, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("path=/api/auth", refreshCookieHeader, StringComparison.OrdinalIgnoreCase);

        var loginBody = await loginResponse.Content.ReadFromJsonAsync<ApiResponse<AuthResponse>>();
        Assert.NotNull(loginBody?.Data);
        Assert.False(string.IsNullOrWhiteSpace(loginBody.Data.RefreshToken));
        Assert.True(loginBody.Data.Expiration <= DateTime.UtcNow.AddMinutes(35));

        var cookieValue = refreshCookieHeader.Split(';')[0];

        using var refreshReq = new HttpRequestMessage(HttpMethod.Post, "/api/auth/refresh");
        refreshReq.Headers.Add("Cookie", cookieValue);
        var refreshRes = await client.SendAsync(refreshReq);
        Assert.Equal(HttpStatusCode.OK, refreshRes.StatusCode);

        var refreshBody = await refreshRes.Content.ReadFromJsonAsync<ApiResponse<AuthResponse>>();
        Assert.NotNull(refreshBody?.Data);
        Assert.NotEqual(loginBody.Data.RefreshToken, refreshBody.Data.RefreshToken);

        // Reusing old refresh token must fail with 401
        using var reuseReq = new HttpRequestMessage(HttpMethod.Post, "/api/auth/refresh");
        reuseReq.Headers.Add("Cookie", cookieValue);
        var reuseRes = await client.SendAsync(reuseReq);
        Assert.Equal(HttpStatusCode.Unauthorized, reuseRes.StatusCode);
    }

    [Fact]
    public async Task ChangePassword_ImmediatelyInvalidatesExistingJwt()
    {
        var (username, password) = await SeedTestUserAsync();

        var loginRes = await _client.PostAsJsonAsync("/api/auth/login", new LoginRequest
        {
            Username = username,
            Password = password
        });
        loginRes.EnsureSuccessStatusCode();
        var loginBody = await loginRes.Content.ReadFromJsonAsync<ApiResponse<AuthResponse>>();
        var token = loginBody!.Data!.AccessToken;

        using var meBefore = new HttpRequestMessage(HttpMethod.Get, "/api/auth/me");
        meBefore.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        var meBeforeRes = await _client.SendAsync(meBefore);
        Assert.Equal(HttpStatusCode.OK, meBeforeRes.StatusCode);

        using var changePwReq = new HttpRequestMessage(HttpMethod.Post, "/api/auth/change-password");
        changePwReq.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        changePwReq.Content = JsonContent.Create(new ChangePasswordRequest
        {
            CurrentPassword = password,
            NewPassword = "NewPassword456!"
        });
        var changePwRes = await _client.SendAsync(changePwReq);
        Assert.Equal(HttpStatusCode.OK, changePwRes.StatusCode);

        using var meAfter = new HttpRequestMessage(HttpMethod.Get, "/api/auth/me");
        meAfter.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        var meAfterRes = await _client.SendAsync(meAfter);
        Assert.Equal(HttpStatusCode.Unauthorized, meAfterRes.StatusCode);
    }

    [Fact]
    public async Task SessionsEndpoints_ListRevokeAndLogoutAll_WorkEndToEnd()
    {
        var (username, password) = await SeedTestUserAsync();

        var login1Res = await _client.PostAsJsonAsync("/api/auth/login", new LoginRequest { Username = username, Password = password });
        var login1 = (await login1Res.Content.ReadFromJsonAsync<ApiResponse<AuthResponse>>())!.Data!;

        var login2Res = await _client.PostAsJsonAsync("/api/auth/login", new LoginRequest { Username = username, Password = password });
        var login2 = (await login2Res.Content.ReadFromJsonAsync<ApiResponse<AuthResponse>>())!.Data!;

        using var listReq = new HttpRequestMessage(HttpMethod.Get, "/api/auth/sessions");
        listReq.Headers.Authorization = new AuthenticationHeaderValue("Bearer", login1.AccessToken);
        var listRes = await _client.SendAsync(listReq);
        Assert.Equal(HttpStatusCode.OK, listRes.StatusCode);
        var sessions = (await listRes.Content.ReadFromJsonAsync<ApiResponse<List<UserSessionDto>>>())!.Data!;
        Assert.Equal(2, sessions.Count);
        Assert.Single(sessions, s => s.IsCurrent && s.Id == login1.SessionId);

        using var deleteReq = new HttpRequestMessage(HttpMethod.Delete, $"/api/auth/sessions/{login2.SessionId}");
        deleteReq.Headers.Authorization = new AuthenticationHeaderValue("Bearer", login1.AccessToken);
        var deleteRes = await _client.SendAsync(deleteReq);
        Assert.Equal(HttpStatusCode.OK, deleteRes.StatusCode);

        using var logoutAllReq = new HttpRequestMessage(HttpMethod.Post, "/api/auth/logout-all");
        logoutAllReq.Headers.Authorization = new AuthenticationHeaderValue("Bearer", login1.AccessToken);
        var logoutAllRes = await _client.SendAsync(logoutAllReq);
        Assert.Equal(HttpStatusCode.OK, logoutAllRes.StatusCode);

        using var meAfter = new HttpRequestMessage(HttpMethod.Get, "/api/auth/me");
        meAfter.Headers.Authorization = new AuthenticationHeaderValue("Bearer", login1.AccessToken);
        var meAfterRes = await _client.SendAsync(meAfter);
        Assert.Equal(HttpStatusCode.Unauthorized, meAfterRes.StatusCode);
    }

    private async Task<(string Username, string Password)> SeedTestUserAsync()
    {
        var username = $"sec_{Guid.NewGuid():N}"[..12];
        const string password = "Password123!";
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<MySocialAppDbContext>();
        var hasher = new PasswordHasher<User>();
        var user = new User
        {
            Username = username,
            Email = $"{username}@test.com",
            DisplayName = "Security Test User",
            Role = Domain.Enums.UserRole.User,
            IsActive = true,
            TokenVersion = 1
        };
        user.PasswordHash = hasher.HashPassword(user, password);
        db.Users.Add(user);
        await db.SaveChangesAsync();
        return (username, password);
    }
}
