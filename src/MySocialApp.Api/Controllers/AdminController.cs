using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using MySocialApp.Application.DTOs.Admin;
using MySocialApp.Application.DTOs.Common;
using MySocialApp.Application.DTOs.Orders;
using MySocialApp.Application.DTOs.Posts;
using MySocialApp.Application.DTOs.Revenue;
using MySocialApp.Application.DTOs.Shops;
using MySocialApp.Application.DTOs.Users;
using MySocialApp.Application.Interfaces;

namespace MySocialApp.Api.Controllers;

[ApiController]
[Route("api/admin")]
[Authorize(Roles = "Admin")]
public class AdminController : ControllerBase
{
    private readonly IAdminService _adminService;
    private readonly IOrderService _orderService;
    private readonly IRevenueService _revenueService;

    public AdminController(
        IAdminService adminService,
        IOrderService orderService,
        IRevenueService revenueService)
    {
        _adminService = adminService;
        _orderService = orderService;
        _revenueService = revenueService;
    }

    [HttpGet("users")]
    public async Task<ActionResult<ApiResponse<PagedResult<UserDto>>>> GetAllUsers(
        [FromQuery] PaginationParams pagination,
        [FromQuery] string? q = null,
        [FromQuery] string? role = null,
        [FromQuery] bool? isActive = null)
    {
        var result = await _adminService.GetAllUsersAsync(pagination, q, role, isActive);
        return Ok(ApiResponse<PagedResult<UserDto>>.SuccessResponse(result));
    }

    [HttpGet("users/{id}")]
    public async Task<ActionResult<ApiResponse<UserDto>>> GetUser(Guid id)
    {
        var result = await _adminService.GetUserByIdAsync(id);
        return Ok(ApiResponse<UserDto>.SuccessResponse(result));
    }

    [HttpGet("users/{id}/detail")]
    public async Task<ActionResult<ApiResponse<AdminUserDetailDto>>> GetUserDetail(Guid id)
    {
        var result = await _adminService.GetUserDetailAsync(id);
        return Ok(ApiResponse<AdminUserDetailDto>.SuccessResponse(result));
    }

    [HttpPost("users")]
    public async Task<ActionResult<ApiResponse<UserDto>>> CreateUser([FromBody] AdminCreateUserRequest request)
    {
        var result = await _adminService.CreateUserAsync(request);
        return Ok(ApiResponse<UserDto>.SuccessResponse(result, "User created."));
    }

    [HttpPut("users/{id}")]
    public async Task<ActionResult<ApiResponse<UserDto>>> UpdateUser(Guid id, [FromBody] AdminUpdateUserRequest request)
    {
        var result = await _adminService.UpdateUserAsync(id, request);
        return Ok(ApiResponse<UserDto>.SuccessResponse(result));
    }

    [HttpDelete("users/{id}")]
    public async Task<ActionResult<ApiResponse>> DeleteUser(Guid id, [FromQuery] bool permanent = false)
    {
        var currentAdminId = GetCurrentUserId();
        await _adminService.DeleteUserAsync(id, permanent, currentAdminId);
        return Ok(ApiResponse.SuccessResponse(permanent ? "User permanently deleted." : "User deactivated."));
    }

    [HttpPatch("users/{id}/toggle-active")]
    public async Task<ActionResult<ApiResponse<UserDto>>> ToggleUserActive(Guid id)
    {
        var currentAdminId = GetCurrentUserId();
        var result = await _adminService.ToggleUserActiveAsync(id, currentAdminId);
        return Ok(ApiResponse<UserDto>.SuccessResponse(result));
    }

    [HttpGet("posts")]
    public async Task<ActionResult<ApiResponse<PagedResult<PostDto>>>> GetAllPosts([FromQuery] PaginationParams pagination)
    {
        var result = await _adminService.GetAllPostsAsync(pagination);
        return Ok(ApiResponse<PagedResult<PostDto>>.SuccessResponse(result));
    }

    [HttpDelete("posts/{id}")]
    public async Task<ActionResult<ApiResponse>> DeletePost(Guid id)
    {
        await _adminService.DeletePostAsync(id);
        return Ok(ApiResponse.SuccessResponse("Post deleted."));
    }

    [HttpGet("shops")]
    public async Task<ActionResult<ApiResponse<PagedResult<ShopDto>>>> GetAllShops([FromQuery] PaginationParams pagination)
    {
        var result = await _adminService.GetAllShopsAsync(pagination);
        return Ok(ApiResponse<PagedResult<ShopDto>>.SuccessResponse(result));
    }

    [HttpPut("shops/{id}/toggle-active")]
    public async Task<ActionResult<ApiResponse<ShopDto>>> ToggleShopActive(Guid id)
    {
        var result = await _adminService.ToggleShopActiveAsync(id);
        return Ok(ApiResponse<ShopDto>.SuccessResponse(result));
    }

    [HttpGet("orders")]
    public async Task<ActionResult<ApiResponse<PagedResult<OrderDto>>>> GetAllOrders(
        [FromQuery] PaginationParams pagination, [FromQuery] string? status)
    {
        var result = await _orderService.GetAllOrdersAsync(pagination, status);
        return Ok(ApiResponse<PagedResult<OrderDto>>.SuccessResponse(result));
    }

    [HttpGet("revenue")]
    public async Task<ActionResult<ApiResponse<RevenueDto>>> GetRevenue([FromQuery] DateTime from, [FromQuery] DateTime to)
    {
        var result = await _revenueService.GetTotalRevenueAsync(from, to);
        return Ok(ApiResponse<RevenueDto>.SuccessResponse(result));
    }

    [HttpGet("shop/revenue")]
    public async Task<ActionResult<ApiResponse<RevenueDto>>> GetShopRevenue(
        [FromQuery] Guid ownerId, [FromQuery] DateTime from, [FromQuery] DateTime to)
    {
        var result = await _revenueService.GetShopRevenueAsync(ownerId, from, to);
        return Ok(ApiResponse<RevenueDto>.SuccessResponse(result));
    }

    private Guid? GetCurrentUserId()
    {
        var claim = User.FindFirstValue(ClaimTypes.NameIdentifier);
        return Guid.TryParse(claim, out var id) ? id : null;
    }
}
