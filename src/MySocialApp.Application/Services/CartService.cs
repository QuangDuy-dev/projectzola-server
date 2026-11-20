using Microsoft.EntityFrameworkCore;
using MySocialApp.Application.DTOs.Cart;
using MySocialApp.Application.Interfaces;
using MySocialApp.Domain.Entities;

namespace MySocialApp.Application.Services;

public class CartService : ICartService
{
    private readonly IApplicationDbContext _context;

    public CartService(IApplicationDbContext context)
    {
        _context = context;
    }

    public async Task<CartDto> GetCartAsync(Guid userId)
    {
        var cart = await GetOrCreateCartAsync(userId);
        return MapToDto(cart);
    }

    public async Task<CartDto> AddItemAsync(Guid userId, AddCartItemRequest request)
    {
        if (request.Quantity <= 0)
            throw new ArgumentOutOfRangeException(nameof(request.Quantity), "Quantity must be greater than zero.");

        var product = await _context.Products
            .Include(p => p.Shop)
            .FirstOrDefaultAsync(p => p.Id == request.ProductId)
            ?? throw new KeyNotFoundException("Product not found.");

        if (!product.IsActive || !product.Shop.IsActive)
            throw new InvalidOperationException("Product is not available.");

        if (product.Stock <= 0)
            throw new InvalidOperationException("Insufficient stock.");

        var cart = await GetOrCreateCartAsync(userId);

        var existingItem = cart.Items.FirstOrDefault(i => i.ProductId == request.ProductId);
        var targetQuantity = (existingItem?.Quantity ?? 0) + request.Quantity;

        if (targetQuantity > product.Stock)
            throw new InvalidOperationException("Insufficient stock.");

        if (existingItem != null)
        {
            existingItem.Quantity = targetQuantity;
        }
        else
        {
            var newItem = new CartItem
            {
                CartId = cart.Id,
                ProductId = request.ProductId,
                Quantity = request.Quantity
            };
            _context.CartItems.Add(newItem);
        }

        await _context.SaveChangesAsync();
        return await GetCartAsync(userId);
    }

    public async Task<CartDto> UpdateItemAsync(Guid userId, Guid itemId, UpdateCartItemRequest request)
    {
        if (request.Quantity <= 0)
            throw new ArgumentOutOfRangeException(nameof(request.Quantity), "Quantity must be greater than zero.");

        var cart = await GetOrCreateCartAsync(userId);
        var item = cart.Items.FirstOrDefault(i => i.Id == itemId)
            ?? throw new KeyNotFoundException("Cart item not found.");

        if (request.Quantity > item.Product.Stock)
            throw new InvalidOperationException("Insufficient stock.");

        item.Quantity = request.Quantity;
        await _context.SaveChangesAsync();
        return await GetCartAsync(userId);
    }

    public async Task RemoveItemAsync(Guid userId, Guid itemId)
    {
        var cart = await GetOrCreateCartAsync(userId);
        var item = cart.Items.FirstOrDefault(i => i.Id == itemId)
            ?? throw new KeyNotFoundException("Cart item not found.");

        _context.CartItems.Remove(item);
        await _context.SaveChangesAsync();
    }

    public async Task ClearCartAsync(Guid userId)
    {
        var cart = await GetOrCreateCartAsync(userId);
        _context.CartItems.RemoveRange(cart.Items);
        await _context.SaveChangesAsync();
    }

    private async Task<Cart> GetOrCreateCartAsync(Guid userId)
    {
        var cart = await _context.Carts
            .Include(c => c.Items)
                .ThenInclude(i => i.Product)
                    .ThenInclude(p => p.Shop)
            .Include(c => c.Items)
                .ThenInclude(i => i.Product)
                    .ThenInclude(p => p.Images)
            .FirstOrDefaultAsync(c => c.UserId == userId);

        if (cart == null)
        {
            cart = new Cart { UserId = userId };
            _context.Carts.Add(cart);
            await _context.SaveChangesAsync();
            cart = await _context.Carts
                .Include(c => c.Items)
                    .ThenInclude(i => i.Product)
                        .ThenInclude(p => p.Shop)
                .Include(c => c.Items)
                    .ThenInclude(i => i.Product)
                        .ThenInclude(p => p.Images)
                .FirstAsync(c => c.UserId == userId);
        }

        return cart;
    }

    private static CartDto MapToDto(Cart cart)
    {
        var items = cart.Items.Select(i => new CartItemDto
        {
            Id = i.Id,
            ProductId = i.ProductId,
            ProductName = i.Product.Name,
            ProductPrice = i.Product.Price,
            UnitPrice = i.Product.Price,
            ProductImageUrl = i.Product.Images.OrderBy(img => img.SortOrder).FirstOrDefault()?.ImageUrl,
            ShopId = i.Product.ShopId,
            ShopName = i.Product.Shop.Name,
            Quantity = i.Quantity,
            Subtotal = i.Product.Price * i.Quantity,
            CurrentStock = i.Product.Stock,
            IsAvailable = i.Product.IsActive && i.Product.Shop.IsActive && i.Product.Stock > 0
        }).ToList();

        var shopGroups = items
            .GroupBy(i => new { i.ShopId, i.ShopName })
            .Select(g => new CartShopGroupDto
            {
                ShopId = g.Key.ShopId,
                ShopName = g.Key.ShopName,
                Items = g.ToList(),
                ShopTotal = g.Sum(x => x.Subtotal)
            }).ToList();

        return new CartDto
        {
            Id = cart.Id,
            Items = items,
            TotalPrice = items.Sum(i => i.Subtotal),
            ShopGroups = shopGroups
        };
    }
}
