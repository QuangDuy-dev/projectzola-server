using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using MySocialApp.Application.DTOs.Common;
using MySocialApp.Application.DTOs.Posts;
using MySocialApp.Application.Interfaces;
using System.Security.Claims;

namespace MySocialApp.Api.Controllers;

[ApiController]
[Route("api/posts")]
public class PostsController : ControllerBase
{
    private readonly IPostService _postService;

    public PostsController(IPostService postService)
    {
        _postService = postService;
    }

    [HttpGet]
    public async Task<ActionResult<ApiResponse<PagedResult<PostDto>>>> GetAll([FromQuery] PaginationParams pagination)
    {
        Guid? currentUserId = User.Identity?.IsAuthenticated == true
            ? Guid.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!)
            : null;
        var result = await _postService.GetPostsAsync(pagination, currentUserId);
        return Ok(ApiResponse<PagedResult<PostDto>>.SuccessResponse(result));
    }

    [HttpGet("{id}")]
    public async Task<ActionResult<ApiResponse<PostDto>>> GetById(Guid id)
    {
        Guid? currentUserId = User.Identity?.IsAuthenticated == true
            ? Guid.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!)
            : null;
        var result = await _postService.GetByIdAsync(id, currentUserId);
        return Ok(ApiResponse<PostDto>.SuccessResponse(result));
    }

    [Authorize]
    [HttpPost]
    public async Task<ActionResult<ApiResponse<PostDto>>> Create([FromBody] CreatePostRequest request)
    {
        var userId = Guid.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);
        var result = await _postService.CreateAsync(userId, request);
        return Created("", ApiResponse<PostDto>.SuccessResponse(result, "Post created."));
    }

    [Authorize]
    [HttpPut("{id}")]
    public async Task<ActionResult<ApiResponse<PostDto>>> Update(Guid id, [FromBody] UpdatePostRequest request)
    {
        var userId = Guid.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);
        var result = await _postService.UpdateAsync(id, userId, request);
        return Ok(ApiResponse<PostDto>.SuccessResponse(result));
    }

    [Authorize]
    [HttpDelete("{id}")]
    public async Task<ActionResult<ApiResponse>> Delete(Guid id)
    {
        var userId = Guid.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);
        await _postService.DeleteAsync(id, userId);
        return Ok(ApiResponse.SuccessResponse("Post deleted."));
    }

    [Authorize]
    [HttpPost("{id}/media")]
    [Consumes("multipart/form-data")]
    [RequestSizeLimit(210_000_000)]
    [RequestFormLimits(MultipartBodyLengthLimit = 210_000_000)]
    public async Task<ActionResult<ApiResponse<PostMediaDto>>> AddMedia(Guid id, IFormFile file)
    {
        if (file == null || file.Length == 0)
            return BadRequest(ApiResponse<PostMediaDto>.ErrorResponse("No file uploaded."));

        var userId = Guid.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);
        using var stream = file.OpenReadStream();
        var result = await _postService.AddMediaAsync(id, userId, stream, file.FileName, file.ContentType, HttpContext.RequestAborted);
        return Created("", ApiResponse<PostMediaDto>.SuccessResponse(result, "Media uploaded successfully."));
    }

    [Authorize]
    [HttpDelete("{id}/media/{mediaId}")]
    public async Task<ActionResult<ApiResponse>> DeleteMedia(Guid id, Guid mediaId)
    {
        var userId = Guid.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);
        await _postService.DeleteMediaAsync(id, mediaId, userId);
        return Ok(ApiResponse.SuccessResponse("Media deleted successfully."));
    }

    [Authorize]
    [HttpPost("{id}/reaction")]
    public async Task<ActionResult<ApiResponse<object>>> ToggleReaction(Guid id, [FromBody] ReactionRequest request)
    {
        var userId = Guid.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);
        var result = await _postService.ToggleReactionAsync(id, userId, request.Type);
        return Ok(ApiResponse<object>.SuccessResponse(
            new { reaction = result },
            result != null ? $"Reaction set to {result}." : "Reaction removed."));
    }

    [Authorize]
    [HttpDelete("{id}/reaction")]
    public async Task<ActionResult<ApiResponse>> RemoveReaction(Guid id)
    {
        var userId = Guid.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);
        await _postService.RemoveReactionAsync(id, userId);
        return Ok(ApiResponse.SuccessResponse("Reaction removed."));
    }
}
