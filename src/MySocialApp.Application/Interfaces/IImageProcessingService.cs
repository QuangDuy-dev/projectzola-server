namespace MySocialApp.Application.Interfaces;

public record ProcessedImageResult(Stream Stream, string ContentType, string Extension);

public interface IImageProcessingService
{
    Task<ProcessedImageResult> ProcessAvatarAsync(Stream inputStream, CancellationToken cancellationToken = default);
    Task<ProcessedImageResult> ProcessImageAsync(Stream inputStream, int maxDimension = 1920, int quality = 80, CancellationToken cancellationToken = default);
}
