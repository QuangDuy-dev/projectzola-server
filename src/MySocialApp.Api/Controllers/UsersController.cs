using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using MySocialApp.Application.DTOs.Common;
using MySocialApp.Application.DTOs.Posts;
using MySocialApp.Application.DTOs.Users;
using MySocialApp.Application.Interfaces;
using System.Security.Claims;

namespace MySocialApp.Api.Controllers;

[ApiController]
[Route("api/users")]
public class UsersController : ControllerBase
{
    private readonly IUserService _userService;
    private readonly IPostService _postService;

    public UsersController(IUserService userService, IPostService postService)
    {
        _userService = userService;
        _postService = postService;
    }

    [HttpGet("{id}")]
    public async Task<ActionResult<ApiResponse<UserDto>>> GetById(Guid id)
    {
        var result = await _userService.GetByIdAsync(id);
        return Ok(ApiResponse<UserDto>.SuccessResponse(result));
    }

    [HttpGet("{id}/profile")]
    public async Task<ActionResult<ApiResponse<PublicUserProfileDto>>> GetPublicProfile(Guid id)
    {
        Guid? currentUserId = User.Identity?.IsAuthenticated == true
            ? Guid.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!)
            : null;

        var result = await _userService.GetPublicProfileAsync(id, currentUserId);
        return Ok(ApiResponse<PublicUserProfileDto>.SuccessResponse(result));
    }

    [HttpGet("by-username/{username}")]
    public async Task<ActionResult<ApiResponse<PublicUserProfileDto>>> GetByUsername(string username)
    {
        Guid? currentUserId = User.Identity?.IsAuthenticated == true
            ? Guid.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!)
            : null;

        var result = await _userService.GetByUsernameAsync(username, currentUserId);
        if (result == null)
            return NotFound(ApiResponse<PublicUserProfileDto>.ErrorResponse($"User '@{username.TrimStart('@')}' not found."));

        return Ok(ApiResponse<PublicUserProfileDto>.SuccessResponse(result));
    }

    [HttpGet("search")]
    public async Task<ActionResult<ApiResponse<List<PublicUserProfileDto>>>> Search([FromQuery] string? q)
    {
        Guid? currentUserId = User.Identity?.IsAuthenticated == true
            ? Guid.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!)
            : null;

        var results = await _userService.SearchUsersAsync(q ?? string.Empty, currentUserId);
        return Ok(ApiResponse<List<PublicUserProfileDto>>.SuccessResponse(results));
    }

    [Authorize]
    [HttpPut("me")]
    public async Task<ActionResult<ApiResponse<UserDto>>> UpdateProfile([FromBody] UpdateUserRequest request)
    {
        var userId = Guid.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);
        var result = await _userService.UpdateProfileAsync(userId, request);
        return Ok(ApiResponse<UserDto>.SuccessResponse(result));
    }

    [Authorize]
    [HttpPost("me/avatar")]
    [Consumes("multipart/form-data")]
    public async Task<ActionResult<ApiResponse<UserDto>>> UpdateAvatar(IFormFile file)
    {
        if (file == null || file.Length == 0)
            return BadRequest(ApiResponse<UserDto>.ErrorResponse("No file uploaded."));

        var userId = Guid.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);
        using var stream = file.OpenReadStream();
        var result = await _userService.UpdateAvatarAsync(userId, stream, file.FileName, file.ContentType);
        return Ok(ApiResponse<UserDto>.SuccessResponse(result, "Avatar updated successfully."));
    }

    [Authorize]
    [HttpPost("{id}/follow")]
    public async Task<ActionResult<ApiResponse>> Follow(Guid id)
    {
        var userId = Guid.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);
        await _userService.FollowAsync(userId, id);
        return Ok(ApiResponse.SuccessResponse("Followed successfully."));
    }

    [Authorize]
    [HttpDelete("{id}/follow")]
    public async Task<ActionResult<ApiResponse>> Unfollow(Guid id)
    {
        var userId = Guid.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);
        await _userService.UnfollowAsync(userId, id);
        return Ok(ApiResponse.SuccessResponse("Unfollowed successfully."));
    }

    [HttpGet("{id}/followers")]
    public async Task<ActionResult<ApiResponse<PagedResult<UserDto>>>> GetFollowers(Guid id, [FromQuery] PaginationParams pagination)
    {
        var result = await _userService.GetFollowersAsync(id, pagination);
        return Ok(ApiResponse<PagedResult<UserDto>>.SuccessResponse(result));
    }

    [HttpGet("{id}/following")]
    public async Task<ActionResult<ApiResponse<PagedResult<UserDto>>>> GetFollowing(Guid id, [FromQuery] PaginationParams pagination)
    {
        var result = await _userService.GetFollowingAsync(id, pagination);
        return Ok(ApiResponse<PagedResult<UserDto>>.SuccessResponse(result));
    }

    [HttpGet("{id}/posts")]
    public async Task<ActionResult<ApiResponse<PagedResult<PostDto>>>> GetUserPosts(Guid id, [FromQuery] PaginationParams pagination)
    {
        Guid? currentUserId = User.Identity?.IsAuthenticated == true
            ? Guid.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!)
            : null;
        var result = await _postService.GetUserPostsAsync(id, pagination, currentUserId);
        return Ok(ApiResponse<PagedResult<PostDto>>.SuccessResponse(result));
    }
}
