namespace MySocialApp.Application.Options;

public class TurnOptions
{
    public const string SectionName = "Turn";

    /// <summary>
    /// Cloudflare Realtime TURN Key ID (1,000 GB free/month, edge in Vietnam/Singapore).
    /// Get from Cloudflare Dashboard -> Realtime -> TURN.
    /// </summary>
    public string? CloudflareKeyId { get; set; }

    /// <summary>
    /// Cloudflare Realtime TURN API Token.
    /// </summary>
    public string? CloudflareApiToken { get; set; }

    /// <summary>
    /// Optional Metered.ca app name (e.g., "myapp" for https://myapp.metered.live).
    /// </summary>
    public string? MeteredAppName { get; set; }

    /// <summary>
    /// Optional Metered.ca API key.
    /// </summary>
    public string? MeteredApiKey { get; set; }

    /// <summary>
    /// Optional static TURN server URLs (e.g., self-hosted Coturn).
    /// </summary>
    public List<string>? StaticUrls { get; set; }
    public string? StaticUsername { get; set; }
    public string? StaticCredential { get; set; }
}
