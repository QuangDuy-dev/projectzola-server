using System.ComponentModel.DataAnnotations;

namespace MySocialApp.Application.DTOs.Auth;

public class SendOtpRequest
{
    [Required]
    [EmailAddress]
    [StringLength(256)]
    public string Email { get; set; } = string.Empty;

    [Required]
    [StringLength(50)]
    public string Purpose { get; set; } = string.Empty;
}
