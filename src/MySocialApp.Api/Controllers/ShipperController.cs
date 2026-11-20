using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using MySocialApp.Application.DTOs.Common;
using MySocialApp.Application.DTOs.Orders;
using MySocialApp.Application.DTOs.Revenue;
using MySocialApp.Application.DTOs.Shipper;
using MySocialApp.Application.Interfaces;
using System.Security.Claims;

namespace MySocialApp.Api.Controllers;

[ApiController]
[Route("api/shipper")]
[Authorize(Roles = "Shipper")]
public class ShipperController : ControllerBase
{
    private readonly IShipperService _shipperService;
    private readonly IOrderService _orderService;
    private readonly IRevenueService _revenueService;

    public ShipperController(
        IShipperService shipperService,
        IOrderService orderService,
        IRevenueService revenueService)
    {
        _shipperService = shipperService;
        _orderService = orderService;
        _revenueService = revenueService;
    }

    [HttpGet("profile")]
    public async Task<ActionResult<ApiResponse<ShipperProfileDto>>> GetProfile()
    {
        var userId = Guid.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);
        var profile = await _shipperService.GetProfileAsync(userId);

        if (profile == null)
            return Ok(ApiResponse<ShipperProfileDto>.SuccessResponse(null!, "No profile found. Please create one."));

        return Ok(ApiResponse<ShipperProfileDto>.SuccessResponse(profile));
    }

    [HttpPut("profile")]
    public async Task<ActionResult<ApiResponse<ShipperProfileDto>>> UpdateProfile([FromBody] UpdateShipperProfileRequest request)
    {
        var userId = Guid.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);
        var result = await _shipperService.UpdateProfileAsync(userId, request);
        return Ok(ApiResponse<ShipperProfileDto>.SuccessResponse(result));
    }

    [HttpGet("orders/available")]
    public async Task<ActionResult<ApiResponse<PagedResult<OrderDto>>>> GetAvailableOrders([FromQuery] PaginationParams pagination)
    {
        var result = await _orderService.GetAvailableForShipperAsync(pagination);
        return Ok(ApiResponse<PagedResult<OrderDto>>.SuccessResponse(result));
    }

    [HttpGet("orders/my")]
    public async Task<ActionResult<ApiResponse<PagedResult<OrderDto>>>> GetMyOrders(
        [FromQuery] PaginationParams pagination,
        [FromQuery] string? status = null)
    {
        var userId = Guid.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);
        var result = await _orderService.GetShipperOrdersAsync(userId, pagination, status);
        return Ok(ApiResponse<PagedResult<OrderDto>>.SuccessResponse(result));
    }

    [HttpPost("orders/{id}/claim")]
    public async Task<ActionResult<ApiResponse>> ClaimOrder(Guid id)
    {
        var userId = Guid.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);
        await _orderService.ClaimAsync(id, userId);
        return Ok(ApiResponse.SuccessResponse("Order claimed."));
    }

    [HttpPost("orders/{id}/pickup")]
    public async Task<ActionResult<ApiResponse>> PickupOrder(Guid id)
    {
        var userId = Guid.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);
        await _orderService.PickupAsync(id, userId);
        return Ok(ApiResponse.SuccessResponse("Order picked up."));
    }

    [HttpPost("orders/{id}/deliver")]
    public async Task<ActionResult<ApiResponse>> DeliverOrder(Guid id)
    {
        var userId = Guid.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);
        await _orderService.DeliverAsync(id, userId);
        return Ok(ApiResponse.SuccessResponse("Order delivered."));
    }

    [HttpGet("revenue")]
    public async Task<ActionResult<ApiResponse<RevenueDto>>> GetRevenue([FromQuery] DateTime from, [FromQuery] DateTime to)
    {
        var userId = Guid.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);
        var result = await _revenueService.GetShipperRevenueAsync(userId, from, to);
        return Ok(ApiResponse<RevenueDto>.SuccessResponse(result));
    }
}
