using System.ComponentModel.DataAnnotations;

namespace MySocialApp.Application.DTOs.Shops;

public class UpdateShopRequest
{
    [StringLength(200, MinimumLength = 1)]
    public string? Name { get; set; }

    [StringLength(2000)]
    public string? Description { get; set; }

    [StringLength(500)]
    public string? AvatarUrl { get; set; }
}
