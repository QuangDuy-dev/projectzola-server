namespace MySocialApp.Application.Interfaces;

public record VideoInspectionResult(bool IsInspected, double? DurationSeconds, int? Width, int? Height, string? Note);

public record AudioInspectionResult(bool IsInspected, double? DurationSeconds);

public interface IMediaProcessingService
{
    bool IsFFmpegAvailable { get; }
    Task<VideoInspectionResult> InspectVideoAsync(string fullPhysicalPath, CancellationToken cancellationToken = default);
    Task<string?> TranscodeVideoAsync(string sourcePhysicalPath, string targetPhysicalPath, CancellationToken cancellationToken = default);
    Task<string?> ExtractVideoThumbnailAsync(string fullPhysicalVideoPath, string outputThumbnailPath, CancellationToken cancellationToken = default);

    Task<AudioInspectionResult> InspectAudioAsync(string filePath, CancellationToken ct = default)
        => Task.FromResult(new AudioInspectionResult(false, null));

    Task<string?> TranscodeAudioToMp3Async(string inputPath, string outputPath, CancellationToken ct = default)
        => Task.FromResult<string?>(null);
}
