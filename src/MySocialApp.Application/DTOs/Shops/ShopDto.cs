namespace MySocialApp.Application.DTOs.Shops;

public class ShopDto
{
    public Guid Id { get; set; }
    public Guid OwnerId { get; set; }
    public string OwnerDisplayName { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }
    public string? AvatarUrl { get; set; }
    public bool IsActive { get; set; }
    public int ProductCount { get; set; }
    public DateTime CreatedAt { get; set; }
}
