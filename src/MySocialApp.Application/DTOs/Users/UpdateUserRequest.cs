using System.ComponentModel.DataAnnotations;

namespace MySocialApp.Application.DTOs.Users;

public class UpdateUserRequest
{
    [StringLength(100)]
    public string? DisplayName { get; set; }

    [StringLength(500)]
    public string? Bio { get; set; }

    [StringLength(500)]
    public string? AvatarUrl { get; set; }
}
