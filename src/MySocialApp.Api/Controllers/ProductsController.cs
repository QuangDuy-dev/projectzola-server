using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using MySocialApp.Application.DTOs.Common;
using MySocialApp.Application.DTOs.Products;
using MySocialApp.Application.Interfaces;
using System.Security.Claims;

namespace MySocialApp.Api.Controllers;

[ApiController]
[Route("api/products")]
public class ProductsController : ControllerBase
{
    private readonly IProductService _productService;

    public ProductsController(IProductService productService)
    {
        _productService = productService;
    }

    [HttpGet]
    public async Task<ActionResult<ApiResponse<PagedResult<ProductDto>>>> GetAll([FromQuery] PaginationParams pagination, [FromQuery] Guid? categoryId)
    {
        var result = await _productService.GetAllAsync(pagination, categoryId);
        return Ok(ApiResponse<PagedResult<ProductDto>>.SuccessResponse(result));
    }

    [HttpGet("{id}")]
    public async Task<ActionResult<ApiResponse<ProductDto>>> GetById(Guid id)
    {
        var result = await _productService.GetByIdAsync(id);
        return Ok(ApiResponse<ProductDto>.SuccessResponse(result));
    }

    [Authorize(Roles = "ShopOwner,Admin")]
    [HttpPut("{id}")]
    public async Task<ActionResult<ApiResponse<ProductDto>>> Update(Guid id, [FromBody] UpdateProductRequest request)
    {
        var userId = Guid.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);
        var result = await _productService.UpdateAsync(id, userId, request);
        return Ok(ApiResponse<ProductDto>.SuccessResponse(result));
    }

    [Authorize(Roles = "ShopOwner,Admin")]
    [HttpDelete("{id}")]
    public async Task<ActionResult<ApiResponse>> Delete(Guid id)
    {
        var userId = Guid.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);
        await _productService.DeleteAsync(id, userId);
        return Ok(ApiResponse.SuccessResponse("Product deleted."));
    }

    [Authorize(Roles = "ShopOwner,Admin")]
    [HttpPost("{id}/images")]
    [Consumes("multipart/form-data")]
    public async Task<ActionResult<ApiResponse<ProductImageDto>>> AddImage(Guid id, IFormFile file)
    {
        if (file == null || file.Length == 0)
            return BadRequest(ApiResponse<ProductImageDto>.ErrorResponse("No file uploaded."));

        var userId = Guid.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);
        using var stream = file.OpenReadStream();
        var result = await _productService.AddImageAsync(id, userId, stream, file.FileName, file.ContentType);
        return Created("", ApiResponse<ProductImageDto>.SuccessResponse(result, "Image uploaded successfully."));
    }

    [Authorize(Roles = "ShopOwner,Admin")]
    [HttpDelete("{id}/images/{imageId}")]
    public async Task<ActionResult<ApiResponse>> DeleteImage(Guid id, Guid imageId)
    {
        var userId = Guid.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);
        await _productService.DeleteImageAsync(id, imageId, userId);
        return Ok(ApiResponse.SuccessResponse("Image deleted successfully."));
    }
}
