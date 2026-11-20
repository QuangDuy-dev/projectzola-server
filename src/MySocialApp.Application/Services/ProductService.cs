using Microsoft.EntityFrameworkCore;
using MySocialApp.Application.DTOs.Common;
using MySocialApp.Application.DTOs.Products;
using MySocialApp.Application.Interfaces;
using MySocialApp.Domain.Entities;

namespace MySocialApp.Application.Services;

public class ProductService : IProductService
{
    private readonly IApplicationDbContext _context;
    private readonly IMediaStorageService? _mediaStorageService;
    private readonly IFileValidator? _fileValidator;
    private readonly IImageProcessingService? _imageProcessingService;

    public ProductService(
        IApplicationDbContext context,
        IMediaStorageService? mediaStorageService = null,
        IFileValidator? fileValidator = null,
        IImageProcessingService? imageProcessingService = null)
    {
        _context = context;
        _mediaStorageService = mediaStorageService;
        _fileValidator = fileValidator;
        _imageProcessingService = imageProcessingService;
    }

    public async Task<PagedResult<ProductDto>> GetAllAsync(PaginationParams pagination, Guid? categoryId = null)
    {
        var query = _context.Products
            .Include(p => p.Shop)
            .Include(p => p.Category)
            .Include(p => p.Images)
            .Where(p => p.IsActive);

        if (categoryId.HasValue)
            query = query.Where(p => p.CategoryId == categoryId.Value);

        var ordered = query.OrderByDescending(p => p.CreatedAt);
        var totalItems = await ordered.CountAsync();
        var products = await ordered
            .Skip((pagination.Page - 1) * pagination.PageSize)
            .Take(pagination.PageSize)
            .ToListAsync();

        return new PagedResult<ProductDto>
        {
            Items = products.Select(MapToDto).ToList(),
            Page = pagination.Page,
            PageSize = pagination.PageSize,
            TotalItems = totalItems
        };
    }

    public async Task<ProductDto> GetByIdAsync(Guid id)
    {
        var product = await _context.Products
            .Include(p => p.Shop)
            .Include(p => p.Category)
            .Include(p => p.Images)
            .FirstOrDefaultAsync(p => p.Id == id)
            ?? throw new KeyNotFoundException("Product not found.");

        return MapToDto(product);
    }

    public async Task<ProductDto> CreateAsync(Guid shopId, Guid ownerId, CreateProductRequest request)
    {
        var shop = await _context.Shops.FindAsync(shopId)
            ?? throw new KeyNotFoundException("Shop not found.");

        if (shop.OwnerId != ownerId)
            throw new UnauthorizedAccessException("Not authorized to add products to this shop.");

        var product = new Product
        {
            ShopId = shopId,
            Name = request.Name,
            Description = request.Description,
            Price = request.Price,
            Stock = request.Stock,
            Sku = request.Sku,
            CategoryId = request.CategoryId
        };

        _context.Products.Add(product);
        await _context.SaveChangesAsync();

        return await GetByIdAsync(product.Id);
    }

    public async Task<ProductDto> UpdateAsync(Guid productId, Guid ownerId, UpdateProductRequest request)
    {
        var product = await _context.Products
            .Include(p => p.Shop)
            .FirstOrDefaultAsync(p => p.Id == productId)
            ?? throw new KeyNotFoundException("Product not found.");

        if (product.Shop.OwnerId != ownerId)
            throw new UnauthorizedAccessException("Not authorized to update this product.");

        if (request.Name != null) product.Name = request.Name;
        if (request.Description != null) product.Description = request.Description;
        if (request.Price.HasValue) product.Price = request.Price.Value;
        if (request.Stock.HasValue) product.Stock = request.Stock.Value;
        if (request.Sku != null) product.Sku = request.Sku;
        if (request.CategoryId.HasValue) product.CategoryId = request.CategoryId;
        if (request.IsActive.HasValue) product.IsActive = request.IsActive.Value;

        await _context.SaveChangesAsync();
        return await GetByIdAsync(productId);
    }

    public async Task DeleteAsync(Guid productId, Guid ownerId)
    {
        var product = await _context.Products
            .Include(p => p.Shop)
            .FirstOrDefaultAsync(p => p.Id == productId)
            ?? throw new KeyNotFoundException("Product not found.");

        if (product.Shop.OwnerId != ownerId)
            throw new UnauthorizedAccessException("Not authorized to delete this product.");

        product.IsActive = false;
        await _context.SaveChangesAsync();
    }

    public async Task<PagedResult<ProductDto>> GetByShopAsync(Guid shopId, PaginationParams pagination, Guid? categoryId = null, string? sort = null)
    {
        if (pagination.Page < 1)
            throw new ArgumentOutOfRangeException(nameof(pagination.Page), "Page must be greater than or equal to 1.");

        if (pagination.PageSize < 1 || pagination.PageSize > 50)
            throw new ArgumentOutOfRangeException(nameof(pagination.PageSize), "PageSize must be between 1 and 50.");

        var query = _context.Products
            .Include(p => p.Shop)
            .Include(p => p.Category)
            .Include(p => p.Images)
            .Where(p => p.ShopId == shopId && p.IsActive);

        if (categoryId.HasValue)
        {
            query = query.Where(p => p.CategoryId == categoryId.Value);
        }

        query = (sort?.ToLowerInvariant()) switch
        {
            "price_asc" => query.OrderBy(p => p.Price).ThenByDescending(p => p.Id),
            "price_desc" => query.OrderByDescending(p => p.Price).ThenByDescending(p => p.Id),
            "name_asc" => query.OrderBy(p => p.Name).ThenByDescending(p => p.Id),
            _ => query.OrderByDescending(p => p.CreatedAt).ThenByDescending(p => p.Id)
        };

        var totalItems = await query.CountAsync();
        var products = await query
            .Skip((pagination.Page - 1) * pagination.PageSize)
            .Take(pagination.PageSize)
            .ToListAsync();

        return new PagedResult<ProductDto>
        {
            Items = products.Select(MapToDto).ToList(),
            Page = pagination.Page,
            PageSize = pagination.PageSize,
            TotalItems = totalItems
        };
    }

    public async Task<ProductImageDto> AddImageAsync(Guid productId, Guid ownerId, Stream fileStream, string fileName, string contentType, CancellationToken cancellationToken = default)
    {
        if (_mediaStorageService == null || _fileValidator == null)
            throw new InvalidOperationException("Media storage or file validator service is not configured.");

        var validation = _fileValidator.Validate(fileStream, fileName, contentType, AllowedMediaType.Image);
        if (!validation.IsValid)
            throw new ArgumentException(validation.ErrorMessage ?? "Invalid product image.");

        var product = await _context.Products
            .Include(p => p.Shop)
            .Include(p => p.Images)
            .FirstOrDefaultAsync(p => p.Id == productId, cancellationToken)
            ?? throw new KeyNotFoundException("Product not found.");

        if (product.Shop.OwnerId != ownerId)
            throw new UnauthorizedAccessException("Not authorized to add images to this product.");

        if (product.Images.Count >= 10)
            throw new InvalidOperationException("A product cannot have more than 10 images.");

        Stream uploadStream = fileStream;
        string uploadContentType = validation.NormalizedContentType ?? contentType;
        string extension = validation.DetectedExtension ?? Path.GetExtension(fileName).ToLowerInvariant();

        ProcessedImageResult? processed = null;
        if (_imageProcessingService != null)
        {
            processed = await _imageProcessingService.ProcessImageAsync(fileStream, cancellationToken: cancellationToken);
            uploadStream = processed.Stream;
            uploadContentType = processed.ContentType;
            extension = processed.Extension;
        }

        var key = $"products/images/{productId}_{Guid.NewGuid():N}{extension}";

        try
        {
            await _mediaStorageService.UploadAsync(uploadStream, key, uploadContentType, cancellationToken);
        }
        finally
        {
            processed?.Stream.Dispose();
        }

        var nextSortOrder = product.Images.Any() ? product.Images.Max(i => i.SortOrder) + 1 : 0;
        var productImage = new ProductImage
        {
            ProductId = productId,
            ImageUrl = _mediaStorageService.GetUrl(key),
            SortOrder = nextSortOrder
        };

        _context.ProductImages.Add(productImage);

        try
        {
            await _context.SaveChangesAsync(cancellationToken);
        }
        catch
        {
            await _mediaStorageService.DeleteAsync(key, cancellationToken);
            throw;
        }

        return new ProductImageDto
        {
            Id = productImage.Id,
            ImageUrl = productImage.ImageUrl,
            SortOrder = productImage.SortOrder
        };
    }

    public async Task DeleteImageAsync(Guid productId, Guid imageId, Guid ownerId, CancellationToken cancellationToken = default)
    {
        var product = await _context.Products
            .Include(p => p.Shop)
            .FirstOrDefaultAsync(p => p.Id == productId, cancellationToken)
            ?? throw new KeyNotFoundException("Product not found.");

        if (product.Shop.OwnerId != ownerId)
            throw new UnauthorizedAccessException("Not authorized to modify images for this product.");

        var image = await _context.ProductImages
            .FirstOrDefaultAsync(i => i.Id == imageId && i.ProductId == productId, cancellationToken)
            ?? throw new KeyNotFoundException("Product image not found.");

        _context.ProductImages.Remove(image);
        await _context.SaveChangesAsync(cancellationToken);

        if (_mediaStorageService != null)
        {
            var key = _mediaStorageService.GetKeyFromUrl(image.ImageUrl);
            if (!string.IsNullOrWhiteSpace(key))
            {
                await _mediaStorageService.DeleteAsync(key, cancellationToken);
            }
        }
    }

    private static ProductDto MapToDto(Product product)
    {
        return new ProductDto
        {
            Id = product.Id,
            ShopId = product.ShopId,
            ShopName = product.Shop.Name,
            Name = product.Name,
            Description = product.Description,
            Price = product.Price,
            Stock = product.Stock,
            Sku = product.Sku,
            CategoryId = product.CategoryId,
            CategoryName = product.Category?.Name,
            IsActive = product.IsActive,
            Images = product.Images.OrderBy(i => i.SortOrder).Select(i => new ProductImageDto
            {
                Id = i.Id,
                ImageUrl = i.ImageUrl,
                SortOrder = i.SortOrder
            }).ToList(),
            CreatedAt = product.CreatedAt
        };
    }
}
