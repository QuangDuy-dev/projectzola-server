using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using MySocialApp.Application.DTOs.Cart;
using MySocialApp.Application.DTOs.Common;
using MySocialApp.Application.Interfaces;
using System.Security.Claims;

namespace MySocialApp.Api.Controllers;

[ApiController]
[Route("api/cart")]
[Authorize]
public class CartController : ControllerBase
{
    private readonly ICartService _cartService;

    public CartController(ICartService cartService)
    {
        _cartService = cartService;
    }

    [HttpGet]
    public async Task<ActionResult<ApiResponse<CartDto>>> GetCart()
    {
        var userId = Guid.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);
        var result = await _cartService.GetCartAsync(userId);
        return Ok(ApiResponse<CartDto>.SuccessResponse(result));
    }

    [HttpPost("items")]
    public async Task<ActionResult<ApiResponse<CartDto>>> AddItem([FromBody] AddCartItemRequest request)
    {
        var userId = Guid.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);
        var result = await _cartService.AddItemAsync(userId, request);
        return Created("", ApiResponse<CartDto>.SuccessResponse(result, "Item added to cart."));
    }

    [HttpPut("items/{id}")]
    public async Task<ActionResult<ApiResponse<CartDto>>> UpdateItem(Guid id, [FromBody] UpdateCartItemRequest request)
    {
        var userId = Guid.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);
        var result = await _cartService.UpdateItemAsync(userId, id, request);
        return Ok(ApiResponse<CartDto>.SuccessResponse(result));
    }

    [HttpDelete("items/{id}")]
    public async Task<ActionResult<ApiResponse>> RemoveItem(Guid id)
    {
        var userId = Guid.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);
        await _cartService.RemoveItemAsync(userId, id);
        return Ok(ApiResponse.SuccessResponse("Item removed from cart."));
    }

    [HttpDelete]
    public async Task<ActionResult<ApiResponse>> ClearCart()
    {
        var userId = Guid.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);
        await _cartService.ClearCartAsync(userId);
        return Ok(ApiResponse.SuccessResponse("Cart cleared."));
    }
}
