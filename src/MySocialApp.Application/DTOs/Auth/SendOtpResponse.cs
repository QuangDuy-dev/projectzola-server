namespace MySocialApp.Application.DTOs.Auth;

public class SendOtpResponse
{
    public string Email { get; set; } = string.Empty;
    public string Purpose { get; set; } = string.Empty;
    public DateTime ExpiresAt { get; set; }
    public bool SentViaEmail { get; set; }
    public string? DevOtpHint { get; set; }
}
