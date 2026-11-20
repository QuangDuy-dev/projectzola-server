using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using MySocialApp.Application.DTOs.Common;
using MySocialApp.Application.DTOs.Feed;
using MySocialApp.Application.Interfaces;
using System.Security.Claims;

namespace MySocialApp.Api.Controllers;

[ApiController]
[Route("api/feed")]
[Authorize]
public class FeedController : ControllerBase
{
    private readonly IFeedService _feedService;

    public FeedController(IFeedService feedService)
    {
        _feedService = feedService;
    }

    [HttpGet]
    public async Task<ActionResult<ApiResponse<CursorPagedResult<FeedPostDto>>>> GetSocialFeed(
        [FromQuery] int limit = 20,
        [FromQuery] string? cursor = null)
    {
        if (limit < 1 || limit > 50)
        {
            return BadRequest(ApiResponse<CursorPagedResult<FeedPostDto>>.ErrorResponse("Limit must be between 1 and 50."));
        }

        var userIdClaim = User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (string.IsNullOrEmpty(userIdClaim) || !Guid.TryParse(userIdClaim, out var currentUserId))
        {
            return Unauthorized(ApiResponse<CursorPagedResult<FeedPostDto>>.ErrorResponse("User is not authenticated."));
        }

        try
        {
            var result = await _feedService.GetSocialFeedAsync(currentUserId, limit, cursor);
            return Ok(ApiResponse<CursorPagedResult<FeedPostDto>>.SuccessResponse(result));
        }
        catch (ArgumentOutOfRangeException ex)
        {
            return BadRequest(ApiResponse<CursorPagedResult<FeedPostDto>>.ErrorResponse(ex.Message));
        }
        catch (ArgumentException ex)
        {
            return BadRequest(ApiResponse<CursorPagedResult<FeedPostDto>>.ErrorResponse(ex.Message));
        }
    }

    [HttpGet("videos")]
    public async Task<ActionResult<ApiResponse<CursorPagedResult<FeedPostDto>>>> GetVideoFeed(
        [FromQuery] int limit = 10,
        [FromQuery] string? cursor = null)
    {
        if (limit < 1 || limit > 50)
        {
            return BadRequest(ApiResponse<CursorPagedResult<FeedPostDto>>.ErrorResponse("Limit must be between 1 and 50."));
        }

        var userIdClaim = User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (string.IsNullOrEmpty(userIdClaim) || !Guid.TryParse(userIdClaim, out var currentUserId))
        {
            return Unauthorized(ApiResponse<CursorPagedResult<FeedPostDto>>.ErrorResponse("User is not authenticated."));
        }

        try
        {
            var result = await _feedService.GetVideoFeedAsync(currentUserId, limit, cursor);
            return Ok(ApiResponse<CursorPagedResult<FeedPostDto>>.SuccessResponse(result));
        }
        catch (ArgumentOutOfRangeException ex)
        {
            return BadRequest(ApiResponse<CursorPagedResult<FeedPostDto>>.ErrorResponse(ex.Message));
        }
        catch (ArgumentException ex)
        {
            return BadRequest(ApiResponse<CursorPagedResult<FeedPostDto>>.ErrorResponse(ex.Message));
        }
    }
}
