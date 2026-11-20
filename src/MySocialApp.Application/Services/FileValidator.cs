using Microsoft.Extensions.Options;
using MySocialApp.Application.Interfaces;
using MySocialApp.Application.Options;

namespace MySocialApp.Application.Services;

public class FileValidator : IFileValidator
{
    private readonly MediaStorageOptions _options;

    private static readonly HashSet<string> DangerousExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".exe", ".dll", ".bat", ".cmd", ".ps1", ".vbs", ".js", ".html", ".htm", ".php", ".phtml",
        ".sh", ".com", ".scr", ".msi", ".jar", ".py", ".rb", ".cgi", ".asp", ".aspx", ".jsp"
    };

    private static readonly Dictionary<string, string> ImageMimeTypes = new(StringComparer.OrdinalIgnoreCase)
    {
        { ".jpg", "image/jpeg" },
        { ".jpeg", "image/jpeg" },
        { ".png", "image/png" },
        { ".webp", "image/webp" }
    };

    private static readonly Dictionary<string, string> VideoMimeTypes = new(StringComparer.OrdinalIgnoreCase)
    {
        { ".mp4", "video/mp4" },
        { ".webm", "video/webm" }
    };

    private static readonly Dictionary<string, string> CanonicalAudioMimeTypes = new(StringComparer.OrdinalIgnoreCase)
    {
        { ".webm", "audio/webm" },
        { ".ogg", "audio/ogg" },
        { ".m4a", "audio/mp4" },
        { ".mp3", "audio/mpeg" },
        { ".wav", "audio/wav" }
    };

    private static readonly Dictionary<string, HashSet<string>> AllowedAudioMimeTypes = new(StringComparer.OrdinalIgnoreCase)
    {
        // Browser MediaRecorder often tags audio-only WebM blobs as video/webm.
        { ".webm", new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "audio/webm", "video/webm" } },
        { ".ogg", new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "audio/ogg" } },
        { ".m4a", new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "audio/mp4", "audio/x-m4a", "audio/m4a" } },
        { ".mp3", new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "audio/mpeg", "audio/mp3" } },
        { ".wav", new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "audio/wav", "audio/wave", "audio/x-wav" } }
    };

    public FileValidator(IOptions<MediaStorageOptions> options)
    {
        _options = options.Value;
    }

    public FileValidationResult Validate(Stream stream, string fileName, string contentType, AllowedMediaType expectedType)
    {
        if (string.IsNullOrWhiteSpace(fileName))
            return Invalid("Filename cannot be empty.");

        var extension = Path.GetExtension(fileName).ToLowerInvariant();

        // 1. Check dangerous extensions
        if (DangerousExtensions.Contains(extension))
            return Invalid($"File extension '{extension}' is strictly prohibited.");

        bool canBeImage = ImageMimeTypes.ContainsKey(extension);
        bool canBeVideo = VideoMimeTypes.ContainsKey(extension);
        bool canBeAudio = CanonicalAudioMimeTypes.ContainsKey(extension);

        if (!canBeImage && !canBeVideo && !canBeAudio)
            return Invalid($"Unsupported file format '{extension}'. Only JPG, PNG, WEBP, MP4, WEBM, OGG, M4A, MP3, and WAV are allowed.");

        string baseContentType = StripMimeParameters(contentType);

        // Resolve effective media category (handling .webm which can be video or audio)
        bool isImage = false;
        bool isVideo = false;
        bool isAudio = false;

        switch (expectedType)
        {
            case AllowedMediaType.Image:
                if (!canBeImage)
                    return Invalid("Only image files (JPG, PNG, WEBP) are allowed.");
                isImage = true;
                break;

            case AllowedMediaType.Video:
                if (!canBeVideo)
                    return Invalid("Only video files (MP4, WEBM) are allowed.");
                isVideo = true;
                break;

            case AllowedMediaType.Audio:
                if (!canBeAudio)
                    return Invalid("Only audio files (WEBM, OGG, M4A, MP3, WAV) are allowed.");
                isAudio = true;
                break;

            case AllowedMediaType.Any:
                if (canBeImage)
                {
                    isImage = true;
                }
                else if (canBeVideo && canBeAudio)
                {
                    // .webm: treat as audio if explicitly declared audio/webm, otherwise video for compatibility
                    if (string.Equals(baseContentType, "audio/webm", StringComparison.OrdinalIgnoreCase))
                        isAudio = true;
                    else
                        isVideo = true;
                }
                else if (canBeVideo)
                {
                    isVideo = true;
                }
                else
                {
                    isAudio = true;
                }
                break;
        }

        // 2. Validate Content-Type / MIME
        string expectedMime;
        bool mimeMatches;

        if (isImage)
        {
            expectedMime = ImageMimeTypes[extension];
            mimeMatches = string.Equals(baseContentType, expectedMime, StringComparison.OrdinalIgnoreCase);
        }
        else if (isVideo)
        {
            expectedMime = VideoMimeTypes[extension];
            mimeMatches = string.Equals(baseContentType, expectedMime, StringComparison.OrdinalIgnoreCase);
        }
        else
        {
            expectedMime = CanonicalAudioMimeTypes[extension];
            mimeMatches = AllowedAudioMimeTypes[extension].Contains(baseContentType);
        }

        if (!mimeMatches && !string.Equals(baseContentType, "application/octet-stream", StringComparison.OrdinalIgnoreCase))
        {
            return Invalid($"Content-Type '{contentType}' does not match file extension '{extension}'. Expected '{expectedMime}'.");
        }

        // 3. Validate File Size
        long maxSizeBytes = isVideo
            ? _options.MaxVideoSizeBytes
            : isAudio
                ? _options.MaxAudioSizeBytes
                : _options.MaxImageSizeBytes;

        int maxMB = isVideo
            ? _options.MaxVideoSizeMB
            : isAudio
                ? _options.MaxAudioSizeMB
                : _options.MaxImageSizeMB;

        if (stream.CanSeek)
        {
            if (stream.Length == 0)
                return Invalid("File is empty.");

            if (stream.Length > maxSizeBytes)
                return Invalid($"File size ({stream.Length / (1024 * 1024)} MB) exceeds the maximum allowed limit of {maxMB} MB.");
        }

        // 4. Validate Magic Bytes / File Header
        if (stream.CanSeek && stream.Length > 0)
        {
            long initialPos = stream.Position;
            byte[] header = new byte[Math.Min(16, (int)stream.Length)];
            int bytesRead = stream.Read(header, 0, header.Length);
            stream.Seek(initialPos, SeekOrigin.Begin);

            if (!ValidateMagicBytes(header, bytesRead, extension))
            {
                return Invalid("File content does not match its declared format (invalid magic bytes).");
            }
        }

        return new FileValidationResult(true, null, extension, expectedMime, isVideo, isAudio);
    }

    private static FileValidationResult Invalid(string message) =>
        new(false, message, null, null, false, false);

    private static string StripMimeParameters(string? contentType)
    {
        if (string.IsNullOrWhiteSpace(contentType))
            return string.Empty;

        var semicolonIndex = contentType.IndexOf(';');
        return (semicolonIndex >= 0 ? contentType[..semicolonIndex] : contentType).Trim();
    }

    private static bool ValidateMagicBytes(byte[] header, int bytesRead, string extension)
    {
        return extension switch
        {
            ".jpg" or ".jpeg" => bytesRead >= 3 &&
                                 header[0] == 0xFF && header[1] == 0xD8 && header[2] == 0xFF,
            ".png" => bytesRead >= 4 &&
                      header[0] == 0x89 && header[1] == 0x50 && header[2] == 0x4E && header[3] == 0x47,
            ".webp" => bytesRead >= 12 &&
                       header[0] == 0x52 && header[1] == 0x49 && header[2] == 0x46 && header[3] == 0x46 && // RIFF
                       header[8] == 0x57 && header[9] == 0x45 && header[10] == 0x42 && header[11] == 0x50, // WEBP
            ".mp4" => bytesRead >= 8 &&
                      header[4] == 0x66 && header[5] == 0x74 && header[6] == 0x79 && header[7] == 0x70, // ftyp
            ".webm" => bytesRead >= 4 &&
                       header[0] == 0x1A && header[1] == 0x45 && header[2] == 0xDF && header[3] == 0xA3, // EBML
            ".ogg" => bytesRead >= 4 &&
                      header[0] == 0x4F && header[1] == 0x67 && header[2] == 0x67 && header[3] == 0x53, // OggS
            ".m4a" => bytesRead >= 8 &&
                      header[4] == 0x66 && header[5] == 0x74 && header[6] == 0x79 && header[7] == 0x70, // ftyp
            ".mp3" => (bytesRead >= 3 &&
                       header[0] == 0x49 && header[1] == 0x44 && header[2] == 0x33) || // ID3
                      (bytesRead >= 2 &&
                       header[0] == 0xFF && (header[1] & 0xE0) == 0xE0), // MPEG frame sync
            ".wav" => bytesRead >= 12 &&
                      header[0] == 0x52 && header[1] == 0x49 && header[2] == 0x46 && header[3] == 0x46 && // RIFF
                      header[8] == 0x57 && header[9] == 0x41 && header[10] == 0x56 && header[11] == 0x45, // WAVE
            _ => false
        };
    }
}
