using System.ComponentModel.DataAnnotations;

namespace MySocialApp.Application.DTOs.Admin;

public class AdminUpdateUserRequest
{
    [StringLength(100)]
    public string? DisplayName { get; set; }

    [EmailAddress]
    [StringLength(256)]
    public string? Email { get; set; }

    [StringLength(500)]
    public string? Bio { get; set; }

    public string? Role { get; set; }
    public bool? IsActive { get; set; }

    [StringLength(100, MinimumLength = 6)]
    public string? NewPassword { get; set; }
}
