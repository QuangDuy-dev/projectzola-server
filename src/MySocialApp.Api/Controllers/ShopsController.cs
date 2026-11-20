using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using MySocialApp.Application.DTOs.Common;
using MySocialApp.Application.DTOs.Products;
using MySocialApp.Application.DTOs.Shops;
using MySocialApp.Application.Interfaces;
using System.Security.Claims;

namespace MySocialApp.Api.Controllers;

[ApiController]
[Route("api/shops")]
public class ShopsController : ControllerBase
{
    private readonly IShopService _shopService;
    private readonly IProductService _productService;

    public ShopsController(IShopService shopService, IProductService productService)
    {
        _shopService = shopService;
        _productService = productService;
    }

    [HttpGet]
    public async Task<ActionResult<ApiResponse<PagedResult<ShopDto>>>> GetAll([FromQuery] PaginationParams pagination)
    {
        var result = await _shopService.GetAllAsync(pagination);
        return Ok(ApiResponse<PagedResult<ShopDto>>.SuccessResponse(result));
    }

    [HttpGet("{id}")]
    public async Task<ActionResult<ApiResponse<ShopDto>>> GetById(Guid id)
    {
        var result = await _shopService.GetByIdAsync(id);
        return Ok(ApiResponse<ShopDto>.SuccessResponse(result));
    }

    [Authorize(Roles = "ShopOwner,Admin")]
    [HttpPost]
    public async Task<ActionResult<ApiResponse<ShopDto>>> Create([FromBody] CreateShopRequest request)
    {
        var userId = Guid.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);
        var result = await _shopService.CreateAsync(userId, request);
        return Created("", ApiResponse<ShopDto>.SuccessResponse(result, "Shop created."));
    }

    [Authorize(Roles = "ShopOwner,Admin")]
    [HttpPut("{id}")]
    public async Task<ActionResult<ApiResponse<ShopDto>>> Update(Guid id, [FromBody] UpdateShopRequest request)
    {
        var userId = Guid.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);
        var result = await _shopService.UpdateAsync(id, userId, request);
        return Ok(ApiResponse<ShopDto>.SuccessResponse(result));
    }

    [HttpGet("{shopId}/products")]
    public async Task<ActionResult<ApiResponse<PagedResult<ProductDto>>>> GetProducts(
        Guid shopId,
        [FromQuery] PaginationParams pagination,
        [FromQuery] Guid? categoryId = null,
        [FromQuery] string? sort = null)
    {
        var result = await _productService.GetByShopAsync(shopId, pagination, categoryId, sort);
        return Ok(ApiResponse<PagedResult<ProductDto>>.SuccessResponse(result));
    }

    [Authorize(Roles = "ShopOwner,Admin")]
    [HttpPost("{shopId}/products")]
    public async Task<ActionResult<ApiResponse<ProductDto>>> CreateProduct(Guid shopId, [FromBody] CreateProductRequest request)
    {
        var userId = Guid.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);
        var result = await _productService.CreateAsync(shopId, userId, request);
        return Created("", ApiResponse<ProductDto>.SuccessResponse(result, "Product created."));
    }
}
