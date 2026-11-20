using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using MySocialApp.Application.DTOs.Comments;
using MySocialApp.Application.DTOs.Common;
using MySocialApp.Application.Interfaces;
using System.Security.Claims;

namespace MySocialApp.Api.Controllers;

[ApiController]
public class CommentsController : ControllerBase
{
    private readonly ICommentService _commentService;

    public CommentsController(ICommentService commentService)
    {
        _commentService = commentService;
    }

    [HttpGet("api/posts/{postId}/comments")]
    public async Task<ActionResult<ApiResponse<PagedResult<CommentDto>>>> GetByPost(Guid postId, [FromQuery] PaginationParams pagination)
    {
        var result = await _commentService.GetByPostAsync(postId, pagination);
        return Ok(ApiResponse<PagedResult<CommentDto>>.SuccessResponse(result));
    }

    [Authorize]
    [HttpPost("api/posts/{postId}/comments")]
    public async Task<ActionResult<ApiResponse<CommentDto>>> Create(Guid postId, [FromBody] CreateCommentRequest request)
    {
        var userId = Guid.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);
        var result = await _commentService.CreateAsync(postId, userId, request);
        return Created("", ApiResponse<CommentDto>.SuccessResponse(result, "Comment created."));
    }

    [Authorize]
    [HttpPut("api/comments/{id}")]
    public async Task<ActionResult<ApiResponse<CommentDto>>> Update(Guid id, [FromBody] UpdateCommentRequest request)
    {
        var userId = Guid.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);
        var result = await _commentService.UpdateAsync(id, userId, request);
        return Ok(ApiResponse<CommentDto>.SuccessResponse(result));
    }

    [Authorize]
    [HttpDelete("api/comments/{id}")]
    public async Task<ActionResult<ApiResponse>> Delete(Guid id)
    {
        var userId = Guid.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);
        var isAdmin = User.IsInRole("Admin");
        await _commentService.DeleteAsync(id, userId, isAdmin);
        return Ok(ApiResponse.SuccessResponse("Comment deleted."));
    }
}
