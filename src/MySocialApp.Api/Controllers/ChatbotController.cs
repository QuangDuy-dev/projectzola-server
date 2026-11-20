using System.Security.Claims;
using System.Text.Json;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using MySocialApp.Application.DTOs.Chatbot;
using MySocialApp.Application.DTOs.Common;
using MySocialApp.Application.Interfaces;

namespace MySocialApp.Api.Controllers;

[ApiController]
[Route("api/chatbot")]
[Authorize]
public class ChatbotController : ControllerBase
{
    private readonly IChatbotService _chatbotService;
    private readonly ILogger<ChatbotController> _logger;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    public ChatbotController(IChatbotService chatbotService, ILogger<ChatbotController> logger)
    {
        _chatbotService = chatbotService;
        _logger = logger;
    }

    [HttpPost("chat/stream")]
    public async Task StreamChat([FromBody] ChatbotRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.Message))
        {
            Response.StatusCode = StatusCodes.Status400BadRequest;
            await Response.WriteAsJsonAsync(ApiResponse.ErrorResponse("Message cannot be empty."));
            return;
        }

        var userId = Guid.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);

        Response.ContentType = "text/event-stream";
        Response.Headers.CacheControl = "no-cache";
        Response.Headers.Connection = "keep-alive";
        Response.Headers["X-Accel-Buffering"] = "no";

        var cancellationToken = HttpContext.RequestAborted;

        try
        {
            await foreach (var chunk in _chatbotService.StreamChatAsync(userId, request.Message, cancellationToken))
            {
                var eventName = chunk.EventType;
                object dataPayload = eventName switch
                {
                    "status" => new { state = chunk.State },
                    "token" => new { text = chunk.Text },
                    "done" => new
                    {
                        category = chunk.Category,
                        durationMs = chunk.DurationMs,
                        timeToFirstTokenMs = chunk.TimeToFirstTokenMs,
                        totalTokens = chunk.TotalTokens
                    },
                    "error" => new { error = chunk.Error },
                    _ => new { text = chunk.Text }
                };

                var json = JsonSerializer.Serialize(dataPayload, JsonOptions);
                var sseMessage = $"event: {eventName}\ndata: {json}\n\n";

                await Response.WriteAsync(sseMessage, cancellationToken);
                await Response.Body.FlushAsync(cancellationToken);
            }
        }
        catch (OperationCanceledException)
        {
            _logger.LogInformation("Client aborted the chatbot streaming connection for user {UserId}.", userId);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Unexpected error during chatbot streaming for user {UserId}.", userId);
            var errorJson = JsonSerializer.Serialize(new { error = "Trợ lý AI gặp lỗi khi xử lý. Vui lòng thử lại sau." }, JsonOptions);
            await Response.WriteAsync($"event: error\ndata: {errorJson}\n\n", CancellationToken.None);
            await Response.Body.FlushAsync(CancellationToken.None);
        }
    }

    [HttpPost("chat")]
    public async Task<ActionResult<ApiResponse<ChatbotResponse>>> Chat([FromBody] ChatbotRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.Message))
            return BadRequest(ApiResponse<ChatbotResponse>.ErrorResponse("Message cannot be empty."));

        var userId = Guid.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);
        var response = await _chatbotService.ChatAsync(userId, request.Message, HttpContext.RequestAborted);
        return Ok(ApiResponse<ChatbotResponse>.SuccessResponse(response));
    }

    [HttpDelete("session")]
    public async Task<ActionResult<ApiResponse>> ClearSession()
    {
        var userId = Guid.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);
        await _chatbotService.ClearSessionAsync(userId);
        return Ok(ApiResponse.SuccessResponse("Chat session cleared."));
    }

    [HttpGet("health")]
    [AllowAnonymous]
    public async Task<ActionResult<ApiResponse<object>>> Health()
    {
        var isHealthy = await _chatbotService.CheckHealthAsync(HttpContext.RequestAborted);
        return Ok(ApiResponse<object>.SuccessResponse(new
        {
            available = isHealthy,
            service = "Ollama"
        }));
    }
}
