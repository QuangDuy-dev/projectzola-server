namespace MySocialApp.Application.Options;

public class MediaStorageOptions
{
    public const string SectionName = "MediaStorage";

    public string Provider { get; set; } = "Local";
    public string RootPath { get; set; } = "../storage";
    public string BaseUrl { get; set; } = "/media";
    public int MaxImageSizeMB { get; set; } = 10;
    public int MaxVideoSizeMB { get; set; } = 200;
    public int MaxAudioSizeMB { get; set; } = 15;

    public bool RequireFFmpeg { get; set; } = false;
    public string? FFmpegPath { get; set; }
    public string? FFprobePath { get; set; }
    public int VideoProcessingTimeoutSeconds { get; set; } = 180;
    public int AvatarMaxDimension { get; set; } = 512;
    public int ImageMaxDimension { get; set; } = 1920;
    public int ImageQuality { get; set; } = 80;
    public int VideoMaxDurationSeconds { get; set; } = 60;
    public int AudioMaxDurationSeconds { get; set; } = 300;
    public int MaxChatImagesPerMessage { get; set; } = 10;

    public long MaxImageSizeBytes => MaxImageSizeMB * 1024L * 1024L;
    public long MaxVideoSizeBytes => MaxVideoSizeMB * 1024L * 1024L;
    public long MaxAudioSizeBytes => MaxAudioSizeMB * 1024L * 1024L;

    public static string ResolveRootPath(string? configuredRootPath, string? contentRootPath = null)
    {
        var rawPath = string.IsNullOrWhiteSpace(configuredRootPath) ? "../storage" : configuredRootPath.Trim();
        if (Path.IsPathRooted(rawPath))
        {
            return Path.GetFullPath(rawPath);
        }

        var startDir = string.IsNullOrWhiteSpace(contentRootPath)
            ? Directory.GetCurrentDirectory()
            : contentRootPath;

        var repoRoot = FindSolutionRoot(startDir);
        if (!string.IsNullOrEmpty(repoRoot))
        {
            return Path.GetFullPath(Path.Combine(repoRoot, rawPath));
        }

        // Published / standalone fallback: keep storage inside the app directory when "../storage" is default
        if (rawPath is "../storage" or @"..\storage")
        {
            return Path.GetFullPath(Path.Combine(startDir, "storage"));
        }

        return Path.GetFullPath(Path.Combine(startDir, rawPath));
    }

    private static string? FindSolutionRoot(string startDir)
    {
        var current = new DirectoryInfo(Path.GetFullPath(startDir));
        for (var i = 0; i < 6 && current != null; i++)
        {
            if (File.Exists(Path.Combine(current.FullName, "MySocialApp.slnx")))
            {
                return current.FullName;
            }
            current = current.Parent;
        }

        return null;
    }
}
