using System.Net.Http.Json;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using MySocialApp.Application.DTOs.Chatbot;
using MySocialApp.Application.Interfaces;
using MySocialApp.Application.Options;

namespace MySocialApp.Infrastructure.Services;

public class OllamaClient : IOllamaClient
{
    private readonly HttpClient _httpClient;
    private readonly OllamaOptions _options;
    private readonly ILogger<OllamaClient> _logger;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    public OllamaClient(
        HttpClient httpClient,
        IOptions<OllamaOptions> options,
        ILogger<OllamaClient> logger)
    {
        _httpClient = httpClient;
        _options = options.Value;
        _logger = logger;
    }

    public async Task<bool> CheckHealthAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            using var timeoutCts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
            using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeoutCts.Token);

            var response = await _httpClient.GetAsync("/api/tags", linkedCts.Token);
            if (!response.IsSuccessStatusCode)
            {
                _logger.LogWarning("Ollama health check returned status {StatusCode}", response.StatusCode);
                return false;
            }

            var tagsResponse = await response.Content.ReadFromJsonAsync<OllamaTagsResponse>(JsonOptions, linkedCts.Token);
            var modelName = _options.Model;
            var hasModel = tagsResponse?.Models?.Any(m =>
                string.Equals(m.Name, modelName, StringComparison.OrdinalIgnoreCase) ||
                string.Equals(m.Model, modelName, StringComparison.OrdinalIgnoreCase)) ?? false;

            if (!hasModel)
            {
                _logger.LogWarning("Ollama is running but model '{Model}' was not found in installed tags", modelName);
            }

            return hasModel;
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or OperationCanceledException)
        {
            _logger.LogWarning("Ollama health check failed: {Message}", ex.Message);
            return false;
        }
    }

    public async IAsyncEnumerable<string> StreamChatAsync(
        List<ChatMessage> messages,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        var requestPayload = new OllamaChatRequest
        {
            model = _options.Model,
            messages = messages.Select(m => new OllamaMessagePayload { role = m.Role, content = m.Content }).ToList(),
            stream = true,
            think = !_options.DisableThinking,
            keep_alive = _options.KeepAlive,
            options = new OllamaParametersPayload
            {
                num_ctx = _options.ContextSize,
                temperature = _options.Temperature,
                num_predict = _options.MaxOutputTokens
            }
        };

        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/chat")
        {
            Content = new StringContent(JsonSerializer.Serialize(requestPayload, JsonOptions), Encoding.UTF8, "application/json")
        };

        HttpResponseMessage? response = null;
        string? failureMessage = null;
        try
        {
            response = await _httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        }
        catch (HttpRequestException ex)
        {
            _logger.LogError(ex, "Failed to connect to Ollama service at {BaseUrl}", _httpClient.BaseAddress);
            failureMessage = "Trợ lý AI hiện không khả dụng. Vui lòng kiểm tra lại dịch vụ Ollama.";
        }
        catch (OperationCanceledException)
        {
            _logger.LogInformation("Ollama streaming request was cancelled by client.");
            yield break;
        }

        if (failureMessage != null)
        {
            yield return failureMessage;
            yield break;
        }

        if (response == null || !response.IsSuccessStatusCode)
        {
            var errorBody = response != null ? await response.Content.ReadAsStringAsync(cancellationToken) : "No response";
            _logger.LogError("Ollama returned error status: {ErrorBody}", errorBody);
            yield return "Trợ lý AI gặp lỗi khi xử lý yêu cầu. Vui lòng thử lại sau.";
            yield break;
        }

        using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        using var reader = new StreamReader(stream, Encoding.UTF8);

        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var line = await reader.ReadLineAsync(cancellationToken);
            if (line == null)
                break;

            if (string.IsNullOrWhiteSpace(line))
                continue;

            OllamaStreamChunk? chunk = null;
            try
            {
                chunk = JsonSerializer.Deserialize<OllamaStreamChunk>(line, JsonOptions);
            }
            catch (JsonException ex)
            {
                _logger.LogWarning(ex, "Failed to deserialize Ollama streaming line: {Line}", line);
                continue;
            }

            if (chunk == null)
                continue;

            if (!string.IsNullOrEmpty(chunk.error))
            {
                _logger.LogError("Ollama chunk error: {Error}", chunk.error);
                yield return $" Lỗi: {chunk.error}";
                yield break;
            }

            // Yield content tokens only (never yield hidden thinking tokens to clients)
            if (!string.IsNullOrEmpty(chunk.message?.content))
            {
                yield return chunk.message.content;
            }

            if (chunk.done)
                break;
        }
    }

    public async Task<string> ChatAsync(List<ChatMessage> messages, CancellationToken cancellationToken = default)
    {
        var sb = new StringBuilder();
        await foreach (var token in StreamChatAsync(messages, cancellationToken))
        {
            sb.Append(token);
        }
        return sb.ToString();
    }

    private class OllamaChatRequest
    {
        public string model { get; set; } = string.Empty;
        public List<OllamaMessagePayload> messages { get; set; } = new();
        public bool stream { get; set; }
        public bool think { get; set; }
        public string keep_alive { get; set; } = "5m";
        public OllamaParametersPayload options { get; set; } = new();
    }

    private class OllamaMessagePayload
    {
        public string role { get; set; } = string.Empty;
        public string content { get; set; } = string.Empty;
    }

    private class OllamaParametersPayload
    {
        public int num_ctx { get; set; }
        public double temperature { get; set; }
        public int num_predict { get; set; }
    }

    private class OllamaStreamChunk
    {
        public string? model { get; set; }
        public OllamaMessagePayload? message { get; set; }
        public bool done { get; set; }
        public string? done_reason { get; set; }
        public string? error { get; set; }
    }

    private class OllamaTagsResponse
    {
        public List<OllamaModelItem>? Models { get; set; }
    }

    private class OllamaModelItem
    {
        public string Name { get; set; } = string.Empty;
        public string Model { get; set; } = string.Empty;
    }
}
