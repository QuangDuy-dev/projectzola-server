using Microsoft.EntityFrameworkCore;
using MySocialApp.Application.DTOs.Common;
using MySocialApp.Application.DTOs.Shops;
using MySocialApp.Application.Interfaces;
using MySocialApp.Domain.Entities;
using MySocialApp.Domain.Enums;

namespace MySocialApp.Application.Services;

public class ShopService : IShopService
{
    private readonly IApplicationDbContext _context;

    public ShopService(IApplicationDbContext context)
    {
        _context = context;
    }

    public async Task<ShopDto> GetByIdAsync(Guid id)
    {
        var shop = await _context.Shops
            .Include(s => s.Owner)
            .Include(s => s.Products)
            .FirstOrDefaultAsync(s => s.Id == id)
            ?? throw new KeyNotFoundException("Shop not found.");

        return MapToDto(shop);
    }

    public async Task<ShopDto> CreateAsync(Guid ownerId, CreateShopRequest request)
    {
        var owner = await _context.Users.FindAsync(ownerId)
            ?? throw new KeyNotFoundException("User not found.");

        if (owner.Role != UserRole.ShopOwner && owner.Role != UserRole.Admin)
            throw new UnauthorizedAccessException("Only ShopOwner or Admin can create shops.");

        var shop = new Shop
        {
            OwnerId = ownerId,
            Name = request.Name,
            Description = request.Description,
            AvatarUrl = request.AvatarUrl
        };

        _context.Shops.Add(shop);
        await _context.SaveChangesAsync();

        return await GetByIdAsync(shop.Id);
    }

    public async Task<ShopDto> UpdateAsync(Guid shopId, Guid ownerId, UpdateShopRequest request)
    {
        var shop = await _context.Shops
            .Include(s => s.Owner)
            .Include(s => s.Products)
            .FirstOrDefaultAsync(s => s.Id == shopId)
            ?? throw new KeyNotFoundException("Shop not found.");

        if (shop.OwnerId != ownerId)
            throw new UnauthorizedAccessException("Not authorized to update this shop.");

        if (request.Name != null) shop.Name = request.Name;
        if (request.Description != null) shop.Description = request.Description;
        if (request.AvatarUrl != null) shop.AvatarUrl = request.AvatarUrl;

        await _context.SaveChangesAsync();
        return MapToDto(shop);
    }

    public async Task<PagedResult<ShopDto>> GetAllAsync(PaginationParams pagination)
    {
        var query = _context.Shops
            .Include(s => s.Owner)
            .Include(s => s.Products)
            .Where(s => s.IsActive)
            .OrderByDescending(s => s.CreatedAt);

        var totalItems = await query.CountAsync();
        var shops = await query
            .Skip((pagination.Page - 1) * pagination.PageSize)
            .Take(pagination.PageSize)
            .ToListAsync();

        return new PagedResult<ShopDto>
        {
            Items = shops.Select(MapToDto).ToList(),
            Page = pagination.Page,
            PageSize = pagination.PageSize,
            TotalItems = totalItems
        };
    }

    public async Task<PagedResult<ShopDto>> GetByOwnerAsync(Guid ownerId, PaginationParams pagination)
    {
        var query = _context.Shops
            .Include(s => s.Owner)
            .Include(s => s.Products)
            .Where(s => s.OwnerId == ownerId)
            .OrderByDescending(s => s.CreatedAt);

        var totalItems = await query.CountAsync();
        var shops = await query
            .Skip((pagination.Page - 1) * pagination.PageSize)
            .Take(pagination.PageSize)
            .ToListAsync();

        return new PagedResult<ShopDto>
        {
            Items = shops.Select(MapToDto).ToList(),
            Page = pagination.Page,
            PageSize = pagination.PageSize,
            TotalItems = totalItems
        };
    }

    private static ShopDto MapToDto(Shop shop)
    {
        return new ShopDto
        {
            Id = shop.Id,
            OwnerId = shop.OwnerId,
            OwnerDisplayName = shop.Owner.DisplayName,
            Name = shop.Name,
            Description = shop.Description,
            AvatarUrl = shop.AvatarUrl,
            IsActive = shop.IsActive,
            ProductCount = shop.Products.Count,
            CreatedAt = shop.CreatedAt
        };
    }
}
