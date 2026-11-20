using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using MySocialApp.Application.DTOs.Common;
using MySocialApp.Application.DTOs.Orders;
using MySocialApp.Application.DTOs.Revenue;
using MySocialApp.Application.Interfaces;
using System.Security.Claims;

namespace MySocialApp.Api.Controllers;

[ApiController]
[Route("api/orders")]
[Authorize]
public class OrdersController : ControllerBase
{
    private readonly IOrderService _orderService;
    private readonly IRevenueService _revenueService;

    public OrdersController(IOrderService orderService, IRevenueService revenueService)
    {
        _orderService = orderService;
        _revenueService = revenueService;
    }

    [HttpPost]
    [HttpPost("checkout")]
    public async Task<ActionResult<ApiResponse<List<OrderDto>>>> Create([FromBody] CreateOrderRequest request)
    {
        var userId = Guid.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);
        var result = await _orderService.CreateAsync(userId, request);
        return Created("", ApiResponse<List<OrderDto>>.SuccessResponse(result, $"{result.Count} order(s) placed successfully."));
    }

    [HttpGet("my")]
    public async Task<ActionResult<ApiResponse<PagedResult<OrderDto>>>> GetMyOrders(
        [FromQuery] PaginationParams pagination,
        [FromQuery] string? status = null)
    {
        var userId = Guid.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);
        var result = await _orderService.GetBuyerOrdersAsync(userId, pagination, status);
        return Ok(ApiResponse<PagedResult<OrderDto>>.SuccessResponse(result));
    }

    [HttpGet("{id}")]
    public async Task<ActionResult<ApiResponse<OrderDto>>> GetById(Guid id)
    {
        var userId = Guid.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);
        var result = await _orderService.GetByIdAsync(id, userId);
        return Ok(ApiResponse<OrderDto>.SuccessResponse(result));
    }

    [HttpPost("{id}/cancel")]
    public async Task<ActionResult<ApiResponse>> Cancel(Guid id)
    {
        var userId = Guid.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);
        await _orderService.CancelAsync(id, userId);
        return Ok(ApiResponse.SuccessResponse("Order cancelled."));
    }

    // Shop Owner endpoints
    [Authorize(Roles = "ShopOwner,Admin")]
    [HttpGet("~/api/shop/orders")]
    public async Task<ActionResult<ApiResponse<PagedResult<OrderDto>>>> GetShopOrders(
        [FromQuery] PaginationParams pagination,
        [FromQuery] string? status = null)
    {
        var userId = Guid.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);
        var result = await _orderService.GetShopOrdersAsync(userId, pagination, status);
        return Ok(ApiResponse<PagedResult<OrderDto>>.SuccessResponse(result));
    }

    [Authorize(Roles = "ShopOwner,Admin")]
    [HttpPost("{id}/confirm")]
    public async Task<ActionResult<ApiResponse>> Confirm(Guid id)
    {
        var userId = Guid.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);
        await _orderService.ConfirmAsync(id, userId);
        return Ok(ApiResponse.SuccessResponse("Order confirmed."));
    }

    [Authorize(Roles = "ShopOwner,Admin")]
    [HttpPost("{id}/prepare")]
    public async Task<ActionResult<ApiResponse>> Prepare(Guid id)
    {
        var userId = Guid.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);
        await _orderService.PrepareAsync(id, userId);
        return Ok(ApiResponse.SuccessResponse("Order is being prepared."));
    }

    [Authorize(Roles = "ShopOwner,Admin")]
    [HttpPost("{id}/ready")]
    public async Task<ActionResult<ApiResponse>> Ready(Guid id)
    {
        var userId = Guid.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);
        await _orderService.ReadyForPickupAsync(id, userId);
        return Ok(ApiResponse.SuccessResponse("Order is ready for pickup."));
    }

    [Authorize(Roles = "ShopOwner,Admin")]
    [HttpGet("~/api/shop/revenue")]
    public async Task<ActionResult<ApiResponse<RevenueDto>>> GetShopRevenue(
        [FromQuery] DateTime from,
        [FromQuery] DateTime to)
    {
        var userId = Guid.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);
        var result = await _revenueService.GetShopRevenueAsync(userId, from, to);
        return Ok(ApiResponse<RevenueDto>.SuccessResponse(result));
    }
}
