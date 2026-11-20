using MySocialApp.Application.DTOs.Common;
using MySocialApp.Application.DTOs.Shops;

namespace MySocialApp.Application.Interfaces;

public interface IShopService
{
    Task<ShopDto> GetByIdAsync(Guid id);
    Task<ShopDto> CreateAsync(Guid ownerId, CreateShopRequest request);
    Task<ShopDto> UpdateAsync(Guid shopId, Guid ownerId, UpdateShopRequest request);
    Task<PagedResult<ShopDto>> GetAllAsync(PaginationParams pagination);
    Task<PagedResult<ShopDto>> GetByOwnerAsync(Guid ownerId, PaginationParams pagination);
}
