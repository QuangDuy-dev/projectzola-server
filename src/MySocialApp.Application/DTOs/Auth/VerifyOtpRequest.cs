using System.ComponentModel.DataAnnotations;

namespace MySocialApp.Application.DTOs.Auth;

public class VerifyOtpRequest
{
    [Required]
    [EmailAddress]
    [StringLength(256)]
    public string Email { get; set; } = string.Empty;

    [Required]
    [StringLength(10, MinimumLength = 4)]
    public string Code { get; set; } = string.Empty;

    [Required]
    [StringLength(50)]
    public string Purpose { get; set; } = string.Empty;
}

public class VerifyOtpResponse
{
    public bool Valid { get; set; }
    public string Message { get; set; } = string.Empty;
}
