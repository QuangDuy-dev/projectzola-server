using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using MySocialApp.Application.Interfaces;
using MySocialApp.Application.Options;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Formats.Webp;
using SixLabors.ImageSharp.Processing;

namespace MySocialApp.Infrastructure.Services;

public class ImageProcessingService : IImageProcessingService
{
    private readonly MediaStorageOptions _options;
    private readonly ILogger<ImageProcessingService> _logger;

    public ImageProcessingService(IOptions<MediaStorageOptions> options, ILogger<ImageProcessingService> logger)
    {
        _options = options.Value;
        _logger = logger;
    }

    public async Task<ProcessedImageResult> ProcessAvatarAsync(Stream inputStream, CancellationToken cancellationToken = default)
    {
        return await ProcessImageAsync(inputStream, _options.AvatarMaxDimension, _options.ImageQuality, cancellationToken);
    }

    public async Task<ProcessedImageResult> ProcessImageAsync(Stream inputStream, int maxDimension = 1920, int quality = 80, CancellationToken cancellationToken = default)
    {
        if (inputStream.CanSeek)
        {
            inputStream.Position = 0;
        }

        using var image = await Image.LoadAsync(inputStream, cancellationToken);

        // 1. Auto-orient based on EXIF orientation
        image.Mutate(x => x.AutoOrient());

        // 2. Scale / Resize if larger than maxDimension (ResizeMode.Max preserves aspect ratio without upscaling)
        if (image.Width > maxDimension || image.Height > maxDimension)
        {
            image.Mutate(x => x.Resize(new ResizeOptions
            {
                Size = new Size(maxDimension, maxDimension),
                Mode = ResizeMode.Max
            }));
        }

        // 3. Strip EXIF & other metadata for privacy (GPS, camera info) and size reduction
        image.Metadata.ExifProfile = null;
        image.Metadata.IccProfile = null;
        image.Metadata.XmpProfile = null;

        // 4. Encode to WebP with target quality factor
        var outputStream = new MemoryStream();
        var encoder = new WebpEncoder
        {
            Quality = quality,
            Method = WebpEncodingMethod.BestQuality
        };

        await image.SaveAsWebpAsync(outputStream, encoder, cancellationToken);
        outputStream.Position = 0;

        _logger.LogInformation("Processed image: scaled to {Width}x{Height}, WebP Quality={Quality}, OutputSize={Size} bytes",
            image.Width, image.Height, quality, outputStream.Length);

        return new ProcessedImageResult(outputStream, "image/webp", ".webp");
    }
}
