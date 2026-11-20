using System.ComponentModel.DataAnnotations;

namespace MySocialApp.Application.DTOs.Admin;

public class AdminCreateUserRequest
{
    [Required]
    [StringLength(50, MinimumLength = 3)]
    public string Username { get; set; } = string.Empty;

    [Required]
    [EmailAddress]
    [StringLength(256)]
    public string Email { get; set; } = string.Empty;

    [Required]
    [StringLength(100)]
    public string DisplayName { get; set; } = string.Empty;

    [Required]
    [StringLength(100, MinimumLength = 6)]
    public string Password { get; set; } = string.Empty;

    [Required]
    public string Role { get; set; } = "User";

    [StringLength(500)]
    public string? Bio { get; set; }

    [StringLength(200)]
    public string? ShopName { get; set; }

    [StringLength(50)]
    public string? VehicleType { get; set; }

    [StringLength(20)]
    public string? LicensePlate { get; set; }
}
