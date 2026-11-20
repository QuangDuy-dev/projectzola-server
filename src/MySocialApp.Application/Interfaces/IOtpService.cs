using MySocialApp.Application.DTOs.Auth;

namespace MySocialApp.Application.Interfaces;

public interface IOtpService
{
    Task<SendOtpResponse> SendOtpAsync(string email, string purpose);
    Task<bool> VerifyOtpAsync(string email, string code, string purpose);
    Task VerifyAndConsumeOtpAsync(string email, string code, string purpose);
}
