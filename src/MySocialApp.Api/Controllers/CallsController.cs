using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Options;
using MySocialApp.Application.DTOs.Common;
using MySocialApp.Application.Options;

namespace MySocialApp.Api.Controllers;

[ApiController]
[Route("api/calls")]
[Authorize]
public class CallsController : ControllerBase
{
    private const string CacheKey = "calls:ice-servers";
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly TurnOptions _turnOptions;
    private readonly IMemoryCache _cache;
    private readonly ILogger<CallsController> _logger;

    public CallsController(
        IHttpClientFactory httpClientFactory,
        IOptions<TurnOptions> turnOptions,
        IMemoryCache cache,
        ILogger<CallsController> logger)
    {
        _httpClientFactory = httpClientFactory;
        _turnOptions = turnOptions.Value;
        _cache = cache;
        _logger = logger;
    }

    [HttpGet("ice-servers")]
    public async Task<ActionResult<ApiResponse<IceServersResponseDto>>> GetIceServers(CancellationToken cancellationToken)
    {
        if (_cache.TryGetValue<IceServersResponseDto>(CacheKey, out var cached) && cached != null)
        {
            return Ok(ApiResponse<IceServersResponseDto>.SuccessResponse(cached));
        }

        var iceServers = new List<IceServerDto>
        {
            new() { Urls = new List<string> { "stun:stun.l.google.com:19302" } },
            new() { Urls = new List<string> { "stun:stun1.l.google.com:19302" } },
            new() { Urls = new List<string> { "stun:stun.cloudflare.com:3478" } },
        };

        bool hasTurn = false;

        // 1. Cloudflare Realtime TURN (Recommended: 1,000 GB free/month, edge in Vietnam/Singapore)
        if (!string.IsNullOrWhiteSpace(_turnOptions.CloudflareKeyId)
            && !string.IsNullOrWhiteSpace(_turnOptions.CloudflareApiToken))
        {
            try
            {
                var client = _httpClientFactory.CreateClient();
                client.Timeout = TimeSpan.FromSeconds(10);
                var url = $"https://rtc.live.cloudflare.com/v1/turn/keys/{_turnOptions.CloudflareKeyId.Trim()}/credentials/generate-ice-servers";
                using var req = new HttpRequestMessage(HttpMethod.Post, url);
                req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _turnOptions.CloudflareApiToken.Trim());
                req.Content = new StringContent("{\"ttl\":86400}", Encoding.UTF8, "application/json");

                using var res = await client.SendAsync(req, cancellationToken);
                if (res.IsSuccessStatusCode)
                {
                    var body = await res.Content.ReadAsStringAsync(cancellationToken);
                    var parsed = JsonSerializer.Deserialize<CloudflareIceResponse>(body, new JsonSerializerOptions
                    {
                        PropertyNameCaseInsensitive = true
                    });

                    if (parsed?.IceServers != null && parsed.IceServers.Count > 0)
                    {
                        foreach (var srv in parsed.IceServers)
                        {
                            iceServers.Add(srv);
                            if (!string.IsNullOrEmpty(srv.Username))
                            {
                                hasTurn = true;
                            }
                        }
                    }
                }
                else
                {
                    _logger.LogWarning("Cloudflare TURN API returned status {StatusCode}", res.StatusCode);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to fetch Cloudflare Realtime TURN credentials");
            }
        }

        // 2. Metered.ca TURN API (Free tier without credit card)
        if (!hasTurn
            && !string.IsNullOrWhiteSpace(_turnOptions.MeteredAppName)
            && !string.IsNullOrWhiteSpace(_turnOptions.MeteredApiKey))
        {
            try
            {
                var client = _httpClientFactory.CreateClient();
                client.Timeout = TimeSpan.FromSeconds(10);
                var url = $"https://{_turnOptions.MeteredAppName.Trim()}.metered.live/api/v1/turn/credentials?apiKey={Uri.EscapeDataString(_turnOptions.MeteredApiKey.Trim())}";
                using var res = await client.GetAsync(url, cancellationToken);
                if (res.IsSuccessStatusCode)
                {
                    var body = await res.Content.ReadAsStringAsync(cancellationToken);
                    using var doc = JsonDocument.Parse(body);
                    if (doc.RootElement.ValueKind == JsonValueKind.Array)
                    {
                        foreach (var el in doc.RootElement.EnumerateArray())
                        {
                            var urls = new List<string>();
                            if (el.TryGetProperty("urls", out var urlsEl))
                            {
                                if (urlsEl.ValueKind == JsonValueKind.String && urlsEl.GetString() is string singleUrl)
                                {
                                    urls.Add(singleUrl);
                                }
                                else if (urlsEl.ValueKind == JsonValueKind.Array)
                                {
                                    foreach (var u in urlsEl.EnumerateArray())
                                    {
                                        if (u.GetString() is string itemUrl) urls.Add(itemUrl);
                                    }
                                }
                            }

                            var username = el.TryGetProperty("username", out var uEl) ? uEl.GetString() : null;
                            var credential = el.TryGetProperty("credential", out var cEl) ? cEl.GetString() : null;

                            if (urls.Count > 0)
                            {
                                iceServers.Add(new IceServerDto
                                {
                                    Urls = urls,
                                    Username = username,
                                    Credential = credential
                                });
                                if (!string.IsNullOrEmpty(username)) hasTurn = true;
                            }
                        }
                    }
                }
                else
                {
                    _logger.LogWarning("Metered TURN API returned status {StatusCode}", res.StatusCode);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to fetch Metered.ca TURN credentials");
            }
        }

        // 3. Static TURN configuration (e.g., self-hosted Coturn)
        if (!hasTurn && _turnOptions.StaticUrls != null && _turnOptions.StaticUrls.Count > 0)
        {
            iceServers.Add(new IceServerDto
            {
                Urls = _turnOptions.StaticUrls,
                Username = _turnOptions.StaticUsername,
                Credential = _turnOptions.StaticCredential
            });
            hasTurn = true;
        }

        var response = new IceServersResponseDto
        {
            IceServers = iceServers,
            HasTurn = hasTurn
        };

        // Cache for 10 minutes if TURN is active (short-lived credentials last 24h)
        if (hasTurn)
        {
            _cache.Set(CacheKey, response, TimeSpan.FromMinutes(10));
        }

        return Ok(ApiResponse<IceServersResponseDto>.SuccessResponse(response));
    }

    private sealed class CloudflareIceResponse
    {
        [JsonPropertyName("iceServers")]
        public List<IceServerDto>? IceServers { get; set; }
    }
}

public class IceServersResponseDto
{
    public List<IceServerDto> IceServers { get; set; } = new();
    public bool HasTurn { get; set; }
}

public class IceServerDto
{
    [JsonPropertyName("urls")]
    public List<string> Urls { get; set; } = new();

    [JsonPropertyName("username")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Username { get; set; }

    [JsonPropertyName("credential")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Credential { get; set; }
}
