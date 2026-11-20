namespace MySocialApp.Application.Interfaces;

public enum AllowedMediaType
{
    Image,
    Video,
    Any,
    Audio
}

public record FileValidationResult(
    bool IsValid,
    string? ErrorMessage,
    string? DetectedExtension,
    string? NormalizedContentType,
    bool IsVideo,
    bool IsAudio = false);

public interface IFileValidator
{
    FileValidationResult Validate(Stream stream, string fileName, string contentType, AllowedMediaType expectedType);
}
