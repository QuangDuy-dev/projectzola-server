using System.Diagnostics;
using System.Globalization;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using MySocialApp.Application.Interfaces;
using MySocialApp.Application.Options;

namespace MySocialApp.Infrastructure.Services;

public class MediaProcessingService : IMediaProcessingService
{
    private readonly ILogger<MediaProcessingService> _logger;
    private readonly bool _isFFmpegAvailable;
    private readonly string _ffmpegPath;
    private readonly string _ffprobePath;
    private readonly int _timeoutSeconds;

    public bool IsFFmpegAvailable => _isFFmpegAvailable;

    public MediaProcessingService(ILogger<MediaProcessingService> logger, IOptions<MediaStorageOptions>? options = null)
    {
        _logger = logger;
        _ffmpegPath = ResolveTool(options?.Value.FFmpegPath, "ffmpeg");
        _ffprobePath = ResolveTool(options?.Value.FFprobePath, "ffprobe");
        _timeoutSeconds = Math.Max(5, options?.Value.VideoProcessingTimeoutSeconds ?? 180);
        _isFFmpegAvailable = CheckTool(_ffmpegPath) && CheckTool(_ffprobePath);

        if (!_isFFmpegAvailable)
        {
            _logger.LogInformation("FFmpeg/ffprobe is not installed on this system. Server-side video duration validation and thumbnail extraction are disabled.");
        }
    }

    public async Task<VideoInspectionResult> InspectVideoAsync(string fullPhysicalPath, CancellationToken cancellationToken = default)
    {
        if (!_isFFmpegAvailable)
        {
            return new VideoInspectionResult(
                IsInspected: false,
                DurationSeconds: null,
                Width: null,
                Height: null,
                Note: "FFmpeg/ffprobe is not installed in the server environment. Duration was not strictly measured."
            );
        }

        try
        {
            var psi = new ProcessStartInfo
            {
                FileName = _ffprobePath,
                Arguments = $"-v error -show_entries format=duration:stream=width,height -of default=noprint_wrappers=1 \"{fullPhysicalPath}\"",
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };

            var (exitCode, output, _) = await RunAsync(psi, cancellationToken);
            if (exitCode != 0) return new VideoInspectionResult(false, null, null, null, "Video could not be inspected.");

            double? duration = null;
            int? width = null;
            int? height = null;

            foreach (var line in output.Split('\n', StringSplitOptions.RemoveEmptyEntries))
            {
                var parts = line.Trim().Split('=');
                if (parts.Length == 2)
                {
                    if (parts[0].Equals("duration", StringComparison.OrdinalIgnoreCase) && double.TryParse(parts[1], NumberStyles.Any, CultureInfo.InvariantCulture, out var d))
                    {
                        duration = d;
                    }
                    else if (parts[0].Equals("width", StringComparison.OrdinalIgnoreCase) && int.TryParse(parts[1], out var w))
                    {
                        width = w;
                    }
                    else if (parts[0].Equals("height", StringComparison.OrdinalIgnoreCase) && int.TryParse(parts[1], out var h))
                    {
                        height = h;
                    }
                }
            }

            return new VideoInspectionResult(duration > 0 && width > 0 && height > 0, duration, width, height, null);
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to inspect video {Path} with ffprobe", fullPhysicalPath);
            return new VideoInspectionResult(false, null, null, null, ex.Message);
        }
    }

    public async Task<string?> TranscodeVideoAsync(string sourcePhysicalPath, string targetPhysicalPath, CancellationToken cancellationToken = default)
    {
        if (!_isFFmpegAvailable) return null;

        try
        {
            var targetDir = Path.GetDirectoryName(targetPhysicalPath);
            if (!string.IsNullOrEmpty(targetDir) && !Directory.Exists(targetDir))
            {
                Directory.CreateDirectory(targetDir);
            }

            // TikTok vertical video optimization:
            // Scale: min(1080, iw), preserve aspect ratio
            // Codec: H.264 (libx264) + AAC
            // Bitrate control: CRF 23, maxrate 2.5M, bufsize 5M
            var psi = new ProcessStartInfo
            {
                FileName = _ffmpegPath,
                Arguments = $"-nostdin -y -i \"{sourcePhysicalPath}\" -map 0:v:0 -map 0:a:0? -vf \"scale=w='min(iw,1080)':h='min(ih,1920)':force_original_aspect_ratio=decrease:force_divisible_by=2\" -c:v libx264 -preset veryfast -crf 23 -maxrate 2.5M -bufsize 5M -pix_fmt yuv420p -c:a aac -b:a 96k -map_metadata -1 -movflags +faststart \"{targetPhysicalPath}\"",
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };

            var (exitCode, _, errorOutput) = await RunAsync(psi, cancellationToken);

            if (exitCode == 0 && File.Exists(targetPhysicalPath) && new FileInfo(targetPhysicalPath).Length > 0)
            {
                _logger.LogInformation("Successfully transcoded video from {Source} to {Target}", sourcePhysicalPath, targetPhysicalPath);
                return targetPhysicalPath;
            }

            _logger.LogWarning("FFmpeg transcoding exited with code {ExitCode}. Error: {Error}", exitCode, errorOutput);
            File.Delete(targetPhysicalPath);
            return null;
        }
        catch (OperationCanceledException) { File.Delete(targetPhysicalPath); throw; }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to transcode video {Path} with ffmpeg", sourcePhysicalPath);
            File.Delete(targetPhysicalPath);
            return null;
        }
    }

    public async Task<string?> ExtractVideoThumbnailAsync(string fullPhysicalVideoPath, string outputThumbnailPath, CancellationToken cancellationToken = default)
    {
        if (!_isFFmpegAvailable) return null;

        try
        {
            var thumbDir = Path.GetDirectoryName(outputThumbnailPath);
            if (!string.IsNullOrEmpty(thumbDir) && !Directory.Exists(thumbDir))
            {
                Directory.CreateDirectory(thumbDir);
            }

            var psi = new ProcessStartInfo
            {
                FileName = _ffmpegPath,
                Arguments = $"-nostdin -y -i \"{fullPhysicalVideoPath}\" -vframes 1 -vf \"scale=480:-2\" -q:v 3 \"{outputThumbnailPath}\"",
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };

            var (exitCode, _, _) = await RunAsync(psi, cancellationToken);
            if (exitCode == 0 && File.Exists(outputThumbnailPath)) return outputThumbnailPath;
            SafeDeleteFile(outputThumbnailPath);
            return null;
        }
        catch (OperationCanceledException) { SafeDeleteFile(outputThumbnailPath); throw; }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to generate thumbnail for video {VideoPath}", fullPhysicalVideoPath);
            return null;
        }
    }

    public async Task<AudioInspectionResult> InspectAudioAsync(string filePath, CancellationToken ct = default)
    {
        if (!_isFFmpegAvailable)
        {
            return new AudioInspectionResult(false, null);
        }

        try
        {
            var psi = new ProcessStartInfo
            {
                FileName = _ffprobePath,
                Arguments = $"-v error -show_entries format=duration:stream=duration -of default=noprint_wrappers=1 \"{filePath}\"",
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };

            var (exitCode, output, _) = await RunAsync(psi, ct);
            if (exitCode != 0)
            {
                return new AudioInspectionResult(false, null);
            }

            double? duration = null;
            foreach (var line in output.Split('\n', StringSplitOptions.RemoveEmptyEntries))
            {
                var parts = line.Trim().Split('=');
                if (parts.Length == 2
                    && parts[0].Equals("duration", StringComparison.OrdinalIgnoreCase)
                    && double.TryParse(parts[1], NumberStyles.Any, CultureInfo.InvariantCulture, out var parsed)
                    && double.IsFinite(parsed)
                    && parsed > 0)
                {
                    duration = parsed;
                }
            }

            return duration.HasValue
                ? new AudioInspectionResult(true, duration.Value)
                : new AudioInspectionResult(false, null);
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to inspect audio {Path} with ffprobe", filePath);
            return new AudioInspectionResult(false, null);
        }
    }

    public async Task<string?> TranscodeAudioToMp3Async(string inputPath, string outputPath, CancellationToken ct = default)
    {
        if (!_isFFmpegAvailable)
        {
            return null;
        }

        try
        {
            var targetDir = Path.GetDirectoryName(outputPath);
            if (!string.IsNullOrEmpty(targetDir) && !Directory.Exists(targetDir))
            {
                Directory.CreateDirectory(targetDir);
            }

            var psi = new ProcessStartInfo
            {
                FileName = _ffmpegPath,
                Arguments = $"-nostdin -y -i \"{inputPath}\" -vn -c:a libmp3lame -b:a 96k \"{outputPath}\"",
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };

            var (exitCode, _, errorOutput) = await RunAsync(psi, ct);

            if (exitCode == 0 && File.Exists(outputPath) && new FileInfo(outputPath).Length > 0)
            {
                _logger.LogInformation("Successfully transcoded audio from {Source} to {Target}", inputPath, outputPath);
                return outputPath;
            }

            _logger.LogWarning("FFmpeg audio transcoding exited with code {ExitCode}. Error: {Error}", exitCode, errorOutput);
            SafeDeleteFile(outputPath);
            return null;
        }
        catch (OperationCanceledException) { SafeDeleteFile(outputPath); throw; }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to transcode audio {Path} with ffmpeg", inputPath);
            SafeDeleteFile(outputPath);
            return null;
        }
    }

    private static void SafeDeleteFile(string path)
    {
        try
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
        catch
        {
            // Best-effort cleanup
        }
    }

    private static string ResolveTool(string? configuredPath, string executable)
    {
        if (!string.IsNullOrWhiteSpace(configuredPath))
            return Path.IsPathRooted(configuredPath) ? configuredPath : Path.Combine(AppContext.BaseDirectory, configuredPath);
        var bundled = Path.Combine(AppContext.BaseDirectory, "tools", executable + (OperatingSystem.IsWindows() ? ".exe" : ""));
        return File.Exists(bundled) ? bundled : executable;
    }

    private async Task<(int Code, string Output, string Error)> RunAsync(ProcessStartInfo info, CancellationToken cancellationToken)
    {
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        deadline.CancelAfter(TimeSpan.FromSeconds(_timeoutSeconds));
        using var process = new Process { StartInfo = info };
        process.Start();
        // Drain both pipes concurrently, including thumbnail stderr, to avoid deadlock.
        var output = process.StandardOutput.ReadToEndAsync();
        var error = process.StandardError.ReadToEndAsync();
        try { await process.WaitForExitAsync(deadline.Token); }
        catch (OperationCanceledException)
        {
            if (!process.HasExited) process.Kill(entireProcessTree: true);
            await process.WaitForExitAsync(CancellationToken.None);
            await Task.WhenAll(output, error);
            cancellationToken.ThrowIfCancellationRequested();
            throw new TimeoutException("Video processing exceeded its time limit.");
        }
        return (process.ExitCode, await output, await error);
    }

    private static bool CheckTool(string executable)
    {
        try
        {
            using var process = new Process
            {
                StartInfo = new ProcessStartInfo
                {
                    FileName = executable,
                    Arguments = "-version",
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                    CreateNoWindow = true
                }
            };
            process.Start();
            var output = process.StandardOutput.ReadToEndAsync();
            var error = process.StandardError.ReadToEndAsync();
            if (!process.WaitForExit(5000)) { process.Kill(entireProcessTree: true); return false; }
            Task.WaitAll(output, error);
            return process.ExitCode == 0;
        }
        catch
        {
            return false;
        }
    }
}
