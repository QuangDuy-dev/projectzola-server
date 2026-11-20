namespace MySocialApp.Application.DTOs.Cart;

public class CartDto
{
    public Guid Id { get; set; }
    public List<CartItemDto> Items { get; set; } = new();
    public decimal TotalPrice { get; set; }
    public decimal CartTotal => TotalPrice;
    public List<CartShopGroupDto> ShopGroups { get; set; } = new();
}

public class CartItemDto
{
    public Guid Id { get; set; }
    public Guid ProductId { get; set; }
    public string ProductName { get; set; } = string.Empty;
    public decimal ProductPrice { get; set; }
    public decimal UnitPrice { get; set; }
    public string? ProductImageUrl { get; set; }
    public Guid ShopId { get; set; }
    public string ShopName { get; set; } = string.Empty;
    public int Quantity { get; set; }
    public decimal Subtotal { get; set; }
    public int CurrentStock { get; set; }
    public bool IsAvailable { get; set; }
}

public class CartShopGroupDto
{
    public Guid ShopId { get; set; }
    public string ShopName { get; set; } = string.Empty;
    public List<CartItemDto> Items { get; set; } = new();
    public decimal ShopTotal { get; set; }
}
