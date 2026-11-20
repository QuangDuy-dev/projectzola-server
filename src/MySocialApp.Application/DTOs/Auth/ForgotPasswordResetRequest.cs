using System.ComponentModel.DataAnnotations;

namespace MySocialApp.Application.DTOs.Auth;

public class ForgotPasswordResetRequest
{
    [Required]
    [EmailAddress]
    [StringLength(256)]
    public string Email { get; set; } = string.Empty;

    [Required]
    [StringLength(10, MinimumLength = 4)]
    public string OtpCode { get; set; } = string.Empty;

    [Required]
    [StringLength(100, MinimumLength = 6)]
    public string NewPassword { get; set; } = string.Empty;
}
