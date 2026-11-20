using System.Security;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using MySocialApp.Application.Interfaces;
using MySocialApp.Application.Options;

namespace MySocialApp.Infrastructure.Services;

public class LocalMediaStorageService : IMediaStorageService
{
    private readonly MediaStorageOptions _options;
    private readonly ILogger<LocalMediaStorageService> _logger;
    private readonly string _canonicalRootPath;

    public LocalMediaStorageService(IOptions<MediaStorageOptions> options, ILogger<LocalMediaStorageService> logger)
    {
        _options = options.Value;
        _logger = logger;
        _canonicalRootPath = Path.GetFullPath(_options.RootPath);

        EnsureStorageDirectories();
    }

    public async Task<string> UploadAsync(Stream fileStream, string storageKey, string contentType, CancellationToken cancellationToken = default)
    {
        var fullPath = GetFullPath(storageKey);
        var directory = Path.GetDirectoryName(fullPath);

        if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
        {
            Directory.CreateDirectory(directory);
        }

        // Stream directly to disk using 80KB buffer to avoid memory spikes
        using var destination = new FileStream(fullPath, FileMode.Create, FileAccess.Write, FileShare.None, 81920, useAsync: true);
        await fileStream.CopyToAsync(destination, cancellationToken);

        _logger.LogInformation("Saved media file to {StorageKey}", storageKey);
        return storageKey.Replace('\\', '/');
    }

    public Task DeleteAsync(string storageKey, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(storageKey))
            return Task.CompletedTask;

        var fullPath = GetFullPath(storageKey);

        try
        {
            if (File.Exists(fullPath))
            {
                File.Delete(fullPath);
                _logger.LogInformation("Deleted physical media file: {StorageKey}", storageKey);
            }
        }
        catch (Exception ex)
        {
            // Missing physical file or file-in-use should not crash business operations
            _logger.LogWarning(ex, "Failed to safely delete physical media file: {StorageKey}", storageKey);
        }

        return Task.CompletedTask;
    }

    public Task<bool> ExistsAsync(string storageKey, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(storageKey))
            return Task.FromResult(false);

        var fullPath = GetFullPath(storageKey);

        try
        {
            return Task.FromResult(File.Exists(fullPath));
        }
        catch
        {
            return Task.FromResult(false);
        }
    }

    public Task<Stream> OpenReadAsync(string storageKey, CancellationToken cancellationToken = default)
    {
        var fullPath = GetFullPath(storageKey);
        if (!File.Exists(fullPath))
            throw new FileNotFoundException($"Media file not found: {storageKey}");

        Stream stream = new FileStream(fullPath, FileMode.Open, FileAccess.Read, FileShare.Read, 81920, useAsync: true);
        return Task.FromResult(stream);
    }

    public string GetUrl(string storageKey)
    {
        if (string.IsNullOrWhiteSpace(storageKey))
            return string.Empty;

        var normalizedKey = storageKey.Replace('\\', '/').TrimStart('/');
        var baseUrl = _options.BaseUrl.TrimEnd('/');
        return $"{baseUrl}/{normalizedKey}";
    }

    public string? GetKeyFromUrl(string url)
    {
        if (string.IsNullOrWhiteSpace(url))
            return null;

        var baseUrl = _options.BaseUrl.TrimEnd('/');
        if (url.StartsWith(baseUrl, StringComparison.OrdinalIgnoreCase))
        {
            return url.Substring(baseUrl.Length).TrimStart('/');
        }

        return url.TrimStart('/');
    }

    private string GetFullPath(string storageKey)
    {
        if (string.IsNullOrWhiteSpace(storageKey))
            throw new ArgumentException("Storage key cannot be null or empty.", nameof(storageKey));

        var sanitizedKey = storageKey
            .Replace('/', Path.DirectorySeparatorChar)
            .Replace('\\', Path.DirectorySeparatorChar)
            .TrimStart(Path.DirectorySeparatorChar);

        var fullPath = Path.GetFullPath(Path.Combine(_canonicalRootPath, sanitizedKey));

        var rootWithSlash = _canonicalRootPath.TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;

        // Strict path traversal defense
        if (!fullPath.StartsWith(rootWithSlash, StringComparison.OrdinalIgnoreCase) &&
            !fullPath.Equals(_canonicalRootPath, StringComparison.OrdinalIgnoreCase))
        {
            _logger.LogWarning("Potential path traversal attempt detected for key: {StorageKey}", storageKey);
            throw new SecurityException($"Access denied: Invalid storage key '{storageKey}'.");
        }

        return fullPath;
    }

    private void EnsureStorageDirectories()
    {
        try
        {
            var dirs = new[]
            {
                Path.Combine(_canonicalRootPath, "avatars"),
                Path.Combine(_canonicalRootPath, "posts", "images"),
                Path.Combine(_canonicalRootPath, "posts", "videos"),
                Path.Combine(_canonicalRootPath, "products", "images"),
                Path.Combine(_canonicalRootPath, "thumbnails"),
                Path.Combine(_canonicalRootPath, "chat", "attachments")
            };

            foreach (var dir in dirs)
            {
                if (!Directory.Exists(dir))
                {
                    Directory.CreateDirectory(dir);
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Could not initialize one or more local storage directories at {RootPath}", _canonicalRootPath);
        }
    }
}
