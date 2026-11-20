using MySocialApp.Application.DTOs.Common;
using MySocialApp.Application.DTOs.Products;

namespace MySocialApp.Application.Interfaces;

public interface IProductService
{
    Task<PagedResult<ProductDto>> GetAllAsync(PaginationParams pagination, Guid? categoryId = null);
    Task<ProductDto> GetByIdAsync(Guid id);
    Task<ProductDto> CreateAsync(Guid shopId, Guid ownerId, CreateProductRequest request);
    Task<ProductDto> UpdateAsync(Guid productId, Guid ownerId, UpdateProductRequest request);
    Task DeleteAsync(Guid productId, Guid ownerId);
    Task<PagedResult<ProductDto>> GetByShopAsync(Guid shopId, PaginationParams pagination, Guid? categoryId = null, string? sort = null);
    Task<ProductImageDto> AddImageAsync(Guid productId, Guid ownerId, Stream fileStream, string fileName, string contentType, CancellationToken cancellationToken = default);
    Task DeleteImageAsync(Guid productId, Guid imageId, Guid ownerId, CancellationToken cancellationToken = default);
}
