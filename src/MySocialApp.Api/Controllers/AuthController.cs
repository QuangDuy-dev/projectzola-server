using System.Security.Claims;
using System.Text.Json;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using MySocialApp.Application.DTOs.Auth;
using MySocialApp.Application.DTOs.Common;
using MySocialApp.Application.Interfaces;

namespace MySocialApp.Api.Controllers;

[ApiController]
[Route("api/auth")]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public class AuthController : ControllerBase
{
    private const string RefreshTokenCookieName = "refreshToken";
    private readonly IAuthService _authService;
    private readonly IOtpService _otpService;

    public AuthController(IAuthService authService, IOtpService otpService)
    {
        _authService = authService;
        _otpService = otpService;
    }

    [HttpPost("send-otp")]
    public async Task<ActionResult<ApiResponse<SendOtpResponse>>> SendOtp([FromBody] SendOtpRequest request)
    {
        var result = await _otpService.SendOtpAsync(request.Email, request.Purpose);
        var message = result.SentViaEmail
            ? "OTP sent to your email."
            : "OTP generated (SMTP not configured; use devOtpHint for testing).";
        return Ok(ApiResponse<SendOtpResponse>.SuccessResponse(result, message));
    }

    [HttpPost("verify-otp")]
    public async Task<ActionResult<ApiResponse<VerifyOtpResponse>>> VerifyOtp([FromBody] VerifyOtpRequest request)
    {
        var valid = await _otpService.VerifyOtpAsync(request.Email, request.Code, request.Purpose);
        if (!valid)
        {
            return BadRequest(ApiResponse<VerifyOtpResponse>.ErrorResponse("Invalid or expired OTP code."));
        }

        return Ok(ApiResponse<VerifyOtpResponse>.SuccessResponse(
            new VerifyOtpResponse { Valid = true, Message = "OTP is valid." },
            "OTP verified."));
    }

    [HttpPost("register")]
    public async Task<ActionResult<ApiResponse<AuthResponse>>> Register([FromBody] RegisterRequest request)
    {
        var result = await _authService.RegisterAsync(request, GetDeviceInfo(), GetIpAddress());
        SetRefreshTokenCookie(result.RefreshToken, result.RefreshTokenExpiration);
        return Created("", ApiResponse<AuthResponse>.SuccessResponse(result, "Registration successful."));
    }

    [HttpPost("login")]
    public async Task<ActionResult<ApiResponse<AuthResponse>>> Login([FromBody] LoginRequest request)
    {
        var result = await _authService.LoginAsync(request, GetDeviceInfo(), GetIpAddress());
        SetRefreshTokenCookie(result.RefreshToken, result.RefreshTokenExpiration);
        return Ok(ApiResponse<AuthResponse>.SuccessResponse(result, "Login successful."));
    }

    [HttpPost("refresh")]
    public async Task<ActionResult<ApiResponse<AuthResponse>>> Refresh()
    {
        var refreshToken = Request.Cookies[RefreshTokenCookieName];
        if (string.IsNullOrWhiteSpace(refreshToken))
        {
            var body = await TryReadJsonAsync<RefreshTokenRequest>();
            refreshToken = body?.RefreshToken;
        }

        if (string.IsNullOrWhiteSpace(refreshToken))
        {
            return Unauthorized(ApiResponse<AuthResponse>.ErrorResponse("Refresh token is required."));
        }

        try
        {
            var result = await _authService.RefreshTokenAsync(refreshToken, GetDeviceInfo(), GetIpAddress());
            SetRefreshTokenCookie(result.RefreshToken, result.RefreshTokenExpiration);
            return Ok(ApiResponse<AuthResponse>.SuccessResponse(result, "Token refreshed."));
        }
        catch (UnauthorizedAccessException ex)
        {
            ClearRefreshTokenCookie();
            return Unauthorized(ApiResponse<AuthResponse>.ErrorResponse(ex.Message));
        }
    }

    [HttpPost("logout")]
    public async Task<ActionResult<ApiResponse>> Logout()
    {
        var refreshToken = Request.Cookies[RefreshTokenCookieName];
        if (string.IsNullOrWhiteSpace(refreshToken))
        {
            var body = await TryReadJsonAsync<RefreshTokenRequest>();
            refreshToken = body?.RefreshToken;
        }

        await _authService.LogoutAsync(refreshToken);
        ClearRefreshTokenCookie();
        return Ok(ApiResponse.SuccessResponse("Logged out successfully."));
    }

    [Authorize]
    [HttpPost("logout-all")]
    public async Task<ActionResult<ApiResponse>> LogoutAll()
    {
        var userId = Guid.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);
        var currentSessionId = GetCurrentSessionId();
        var body = await TryReadJsonAsync<LogoutAllRequest>();
        var keepCurrentSession = body?.KeepCurrentSession ?? false;

        await _authService.LogoutAllAsync(userId, currentSessionId, keepCurrentSession);
        if (!keepCurrentSession)
        {
            ClearRefreshTokenCookie();
        }

        return Ok(ApiResponse.SuccessResponse("Logged out from sessions."));
    }

    [Authorize]
    [HttpGet("sessions")]
    public async Task<ActionResult<ApiResponse<IReadOnlyList<UserSessionDto>>>> GetSessions()
    {
        var userId = Guid.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);
        var currentSessionId = GetCurrentSessionId();
        var sessions = await _authService.GetActiveSessionsAsync(userId, currentSessionId);
        return Ok(ApiResponse<IReadOnlyList<UserSessionDto>>.SuccessResponse(sessions));
    }

    [Authorize]
    [HttpDelete("sessions/{sessionId:guid}")]
    public async Task<ActionResult<ApiResponse>> RevokeSession(Guid sessionId)
    {
        var userId = Guid.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);
        await _authService.RevokeSessionAsync(userId, sessionId);
        return Ok(ApiResponse.SuccessResponse("Session revoked."));
    }

    [HttpPost("forgot-password/reset")]
    public async Task<ActionResult<ApiResponse>> ForgotPasswordReset([FromBody] ForgotPasswordResetRequest request)
    {
        await _authService.ForgotPasswordResetAsync(request);
        ClearRefreshTokenCookie();
        return Ok(ApiResponse.SuccessResponse("Password has been reset successfully."));
    }

    [Authorize]
    [HttpPost("change-password")]
    public async Task<ActionResult<ApiResponse>> ChangePassword([FromBody] ChangePasswordRequest request)
    {
        var userId = Guid.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);
        await _authService.ChangePasswordAsync(userId, request);
        ClearRefreshTokenCookie();
        return Ok(ApiResponse.SuccessResponse("Password changed successfully."));
    }

    [Authorize]
    [HttpGet("me")]
    public async Task<ActionResult<ApiResponse<UserInfoDto>>> GetMe()
    {
        var userId = Guid.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);
        var result = await _authService.GetCurrentUserAsync(userId);
        return Ok(ApiResponse<UserInfoDto>.SuccessResponse(result));
    }

    private void SetRefreshTokenCookie(string refreshToken, DateTime expiresAt)
    {
        var isHttps = Request.IsHttps;
        Response.Cookies.Append(RefreshTokenCookieName, refreshToken, new CookieOptions
        {
            HttpOnly = true,
            Secure = isHttps,
            SameSite = isHttps ? SameSiteMode.None : SameSiteMode.Lax,
            Path = "/api/auth",
            Expires = expiresAt
        });
    }

    private void ClearRefreshTokenCookie()
    {
        var isHttps = Request.IsHttps;
        Response.Cookies.Delete(RefreshTokenCookieName, new CookieOptions
        {
            HttpOnly = true,
            Secure = isHttps,
            SameSite = isHttps ? SameSiteMode.None : SameSiteMode.Lax,
            Path = "/api/auth"
        });
    }

    private string? GetDeviceInfo()
    {
        return Request.Headers.UserAgent.ToString();
    }

    private string? GetIpAddress()
    {
        return HttpContext.Connection.RemoteIpAddress?.ToString();
    }

    private Guid? GetCurrentSessionId()
    {
        var claim = User.FindFirstValue("session_id");
        return Guid.TryParse(claim, out var sessionId) ? sessionId : null;
    }

    private async Task<T?> TryReadJsonAsync<T>() where T : class
    {
        if (!Request.HasJsonContentType() || Request.ContentLength == 0)
            return null;

        try
        {
            return await Request.ReadFromJsonAsync<T>();
        }
        catch (JsonException)
        {
            return null;
        }
    }
}
