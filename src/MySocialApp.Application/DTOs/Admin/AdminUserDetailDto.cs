namespace MySocialApp.Application.DTOs.Admin;

public class AdminUserDetailDto
{
    public Guid Id { get; set; }
    public string Username { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    public string DisplayName { get; set; } = string.Empty;
    public string? AvatarUrl { get; set; }
    public string? Bio { get; set; }
    public string Role { get; set; } = string.Empty;
    public bool IsActive { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }

    // User role stats
    public int PostsCount { get; set; }
    public int FollowersCount { get; set; }
    public int FollowingCount { get; set; }
    public int BuyerOrdersCount { get; set; }
    public decimal TotalSpent { get; set; }
    public List<AdminUserOrderSummaryDto> RecentOrders { get; set; } = new();

    // ShopOwner role data
    public List<AdminShopSummaryDto>? Shops { get; set; }

    // Shipper role data
    public AdminShipperProfileDto? ShipperProfile { get; set; }
    public int? ActiveDeliveriesCount { get; set; }

    // Admin role data
    public List<string>? AdminPermissions { get; set; }
    public string? AdminPermissionsSummary { get; set; }
}

public class AdminUserOrderSummaryDto
{
    public Guid Id { get; set; }
    public Guid ShopId { get; set; }
    public string ShopName { get; set; } = string.Empty;
    public decimal TotalPrice { get; set; }
    public decimal ShippingFee { get; set; }
    public string Status { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; }
}

public class AdminShopSummaryDto
{
    public Guid Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }
    public bool IsActive { get; set; }
    public int ProductCount { get; set; }
    public int OrderCount { get; set; }
    public decimal TotalRevenue { get; set; }
}

public class AdminShipperProfileDto
{
    public string? VehicleType { get; set; }
    public string? LicensePlate { get; set; }
    public bool IsAvailable { get; set; }
    public int TotalDeliveries { get; set; }
}
