using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using MySocialApp.Application.DTOs.Chat;
using MySocialApp.Application.Interfaces;
using MySocialApp.Application.Options;
using MySocialApp.Domain.Entities;
using MySocialApp.Domain.Enums;

namespace MySocialApp.Application.Services;

public class ChatMediaService : IChatMediaService
{
    private const int MaxWaveformSamples = 128;

    private readonly IApplicationDbContext _context;
    private readonly IMediaStorageService _mediaStorageService;
    private readonly IFileValidator _fileValidator;
    private readonly IMediaProcessingService? _mediaProcessingService;
    private readonly IImageProcessingService? _imageProcessingService;
    private readonly MediaStorageOptions _options;

    public ChatMediaService(
        IApplicationDbContext context,
        IMediaStorageService mediaStorageService,
        IFileValidator fileValidator,
        IMediaProcessingService? mediaProcessingService = null,
        IImageProcessingService? imageProcessingService = null,
        IOptions<MediaStorageOptions>? storageOptions = null)
    {
        _context = context;
        _mediaStorageService = mediaStorageService;
        _fileValidator = fileValidator;
        _mediaProcessingService = mediaProcessingService;
        _imageProcessingService = imageProcessingService;
        _options = storageOptions?.Value ?? new MediaStorageOptions();
    }

    public async Task<MessageDto> SendMediaMessageAsync(
        Guid conversationId,
        Guid senderId,
        IReadOnlyList<SendMediaMessageItem> files,
        string? caption = null,
        Guid? replyToMessageId = null,
        List<Guid>? mentionedUserIds = null,
        bool mentionAll = false,
        string? clientMessageId = null,
        CancellationToken ct = default)
    {
        var (conversation, _) = await EnsureCanSendAsync(conversationId, senderId, ct);

        // Idempotency check using ClientMessageId
        if (!string.IsNullOrWhiteSpace(clientMessageId))
        {
            var existingMessage = await ChatService.IncludeFullMessageDetails(_context.Messages)
                .FirstOrDefaultAsync(m => m.ConversationId == conversationId
                                          && m.SenderId == senderId
                                          && m.ClientMessageId == clientMessageId, ct);

            if (existingMessage != null)
                return ChatService.MapMessageToDto(existingMessage, senderId);
        }

        if (caption != null && caption.Length > 4000)
            throw new ArgumentException("Caption cannot exceed 4000 characters.");

        if (replyToMessageId.HasValue)
        {
            var replyExists = await _context.Messages
                .AnyAsync(m => m.Id == replyToMessageId.Value && m.ConversationId == conversationId, ct);

            if (!replyExists)
                throw new KeyNotFoundException("Reply target message not found in this conversation.");
        }

        if (files == null || files.Count == 0)
            throw new ArgumentException("At least one media file is required.");

        var maxImages = Math.Max(1, _options.MaxChatImagesPerMessage);
        if (files.Count > maxImages)
            throw new ArgumentException($"A message cannot contain more than {maxImages} images.");

        var validatedItems = new List<(SendMediaMessageItem Item, FileValidationResult Validation)>(files.Count);
        int imageCount = 0;
        int videoCount = 0;

        foreach (var item in files)
        {
            if (item == null || item.Content == null)
                throw new ArgumentException("Media file stream cannot be null.");

            var validation = _fileValidator.Validate(item.Content, item.FileName, item.ContentType, AllowedMediaType.Any);
            if (!validation.IsValid)
                throw new ArgumentException(validation.ErrorMessage ?? "Invalid media file.");

            if (validation.IsAudio)
                throw new ArgumentException("Audio files must be sent as voice messages.");

            if (validation.IsVideo)
                videoCount++;
            else
                imageCount++;

            validatedItems.Add((item, validation));
        }

        if (videoCount > 0 && imageCount > 0)
            throw new ArgumentException("Cannot mix images and videos in the same message.");

        if (videoCount > 1)
            throw new ArgumentException("A message can contain at most 1 video.");

        if (imageCount > maxImages)
            throw new ArgumentException($"A message cannot contain more than {maxImages} images.");

        bool isVideoMessage = videoCount == 1;
        if (isVideoMessage && _options.RequireFFmpeg && !(_mediaProcessingService?.IsFFmpegAvailable ?? false))
        {
            throw new InvalidOperationException("FFmpeg/ffprobe is required for video processing but is not available on this server.");
        }

        var effectiveMentionAll = conversation.Type == ConversationType.Group && mentionAll;
        var mentionedUserIdsJson = ChatService.SerializeMentionedUserIds(mentionedUserIds);

        var uploadedKeys = new List<string>();
        var attachments = new List<MessageAttachment>();

        try
        {
            if (isVideoMessage)
            {
                var (item, validation) = validatedItems[0];
                var attachment = await ProcessAndUploadVideoAsync(conversationId, item, validation, uploadedKeys, ct);
                attachments.Add(attachment);
            }
            else
            {
                for (int i = 0; i < validatedItems.Count; i++)
                {
                    var (item, validation) = validatedItems[i];
                    var attachment = await ProcessAndUploadImageAsync(conversationId, item, validation, i, uploadedKeys, ct);
                    attachments.Add(attachment);
                }
            }

            var message = new Message
            {
                ConversationId = conversationId,
                SenderId = senderId,
                Type = isVideoMessage ? MessageType.Video : MessageType.Image,
                Content = caption?.Trim() ?? string.Empty,
                ClientMessageId = clientMessageId,
                ReplyToMessageId = replyToMessageId,
                MentionedUserIdsJson = mentionedUserIdsJson,
                MentionAll = effectiveMentionAll,
                Attachments = attachments
            };

            _context.Messages.Add(message);
            conversation.UpdatedAt = DateTime.UtcNow;

            await _context.SaveChangesAsync(ct);

            var saved = await ChatService.IncludeFullMessageDetails(_context.Messages)
                .FirstAsync(m => m.Id == message.Id, ct);

            return ChatService.MapMessageToDto(saved, senderId);
        }
        catch
        {
            await RollbackUploadedFilesAsync(uploadedKeys);
            throw;
        }
    }

    public async Task<MessageDto> SendVoiceMessageAsync(
        Guid conversationId,
        Guid senderId,
        SendVoiceMessageInput input,
        CancellationToken ct = default)
    {
        if (input == null || input.Content == null)
            throw new ArgumentException("Audio file is required.");

        var (conversation, _) = await EnsureCanSendAsync(conversationId, senderId, ct);

        // Idempotency check using ClientMessageId
        if (!string.IsNullOrWhiteSpace(input.ClientMessageId))
        {
            var existingMessage = await ChatService.IncludeFullMessageDetails(_context.Messages)
                .FirstOrDefaultAsync(m => m.ConversationId == conversationId
                                          && m.SenderId == senderId
                                          && m.ClientMessageId == input.ClientMessageId, ct);

            if (existingMessage != null)
                return ChatService.MapMessageToDto(existingMessage, senderId);
        }

        if (input.ReplyToMessageId.HasValue)
        {
            var replyExists = await _context.Messages
                .AnyAsync(m => m.Id == input.ReplyToMessageId.Value && m.ConversationId == conversationId, ct);

            if (!replyExists)
                throw new KeyNotFoundException("Reply target message not found in this conversation.");
        }

        var validation = _fileValidator.Validate(input.Content, input.FileName, input.ContentType, AllowedMediaType.Audio);
        if (!validation.IsValid || !validation.IsAudio)
            throw new ArgumentException(validation.ErrorMessage ?? "Invalid audio file.");

        var maxAudioDuration = Math.Max(1, _options.AudioMaxDurationSeconds);
        double? durationSeconds = input.DurationSeconds;
        if (durationSeconds.HasValue)
        {
            if (!double.IsFinite(durationSeconds.Value) || durationSeconds.Value <= 0 || durationSeconds.Value > maxAudioDuration)
            {
                throw new ArgumentException($"Voice duration must be greater than 0 and at most {maxAudioDuration} seconds.");
            }

            durationSeconds = Math.Round(durationSeconds.Value, 2);
        }

        var sanitizedWaveform = SanitizeWaveformJson(input.WaveformJson);

        var uploadedKeys = new List<string>();

        try
        {
            if (input.Content.CanSeek)
                input.Content.Position = 0;

            var extension = validation.DetectedExtension ?? Path.GetExtension(input.FileName).ToLowerInvariant();
            var mimeType = validation.NormalizedContentType ?? "audio/webm";
            long fileSizeBytes = input.Content.CanSeek ? input.Content.Length : 0;

            var storageKey = $"chat/voices/{conversationId}_{Guid.NewGuid():N}{extension}";
            uploadedKeys.Add(storageKey);

            await _mediaStorageService.UploadAsync(input.Content, storageKey, mimeType, ct);

            var physicalAudioPath = Path.Combine(_options.RootPath, storageKey);
            if (File.Exists(physicalAudioPath))
            {
                fileSizeBytes = new FileInfo(physicalAudioPath).Length;
            }

            if (_mediaProcessingService != null && _mediaProcessingService.IsFFmpegAvailable && File.Exists(physicalAudioPath))
            {
                var inspection = await _mediaProcessingService.InspectAudioAsync(physicalAudioPath, ct);
                if (inspection.IsInspected && inspection.DurationSeconds.HasValue)
                {
                    if (inspection.DurationSeconds.Value > maxAudioDuration)
                    {
                        throw new InvalidOperationException(
                            $"Voice duration ({inspection.DurationSeconds.Value:F1}s) exceeds the maximum allowed duration of {maxAudioDuration} seconds.");
                    }

                    durationSeconds = Math.Round(inspection.DurationSeconds.Value, 2);
                }

                var mp3Key = $"chat/voices/{conversationId}_{Guid.NewGuid():N}.mp3";
                var mp3Path = Path.Combine(_options.RootPath, mp3Key);
                uploadedKeys.Add(mp3Key);

                var transcodedPath = await _mediaProcessingService.TranscodeAudioToMp3Async(physicalAudioPath, mp3Path, ct);
                if (transcodedPath != null && File.Exists(mp3Path) && new FileInfo(mp3Path).Length > 0)
                {
                    await _mediaStorageService.DeleteAsync(storageKey, CancellationToken.None);
                    uploadedKeys.Remove(storageKey);

                    storageKey = mp3Key;
                    mimeType = "audio/mpeg";
                    fileSizeBytes = new FileInfo(mp3Path).Length;
                }
                else
                {
                    await _mediaStorageService.DeleteAsync(mp3Key, CancellationToken.None);
                    uploadedKeys.Remove(mp3Key);
                }
            }

            var attachment = new MessageAttachment
            {
                AttachmentType = AttachmentType.Audio,
                StorageUrl = _mediaStorageService.GetUrl(storageKey),
                MimeType = mimeType,
                OriginalFileName = TruncateFileName(input.FileName, 260),
                FileSizeBytes = fileSizeBytes,
                DurationSeconds = durationSeconds,
                WaveformJson = sanitizedWaveform,
                SortOrder = 0
            };

            var message = new Message
            {
                ConversationId = conversationId,
                SenderId = senderId,
                Type = MessageType.Voice,
                Content = string.Empty,
                ClientMessageId = input.ClientMessageId,
                ReplyToMessageId = input.ReplyToMessageId,
                Attachments = new List<MessageAttachment> { attachment }
            };

            _context.Messages.Add(message);
            conversation.UpdatedAt = DateTime.UtcNow;

            await _context.SaveChangesAsync(ct);

            var saved = await ChatService.IncludeFullMessageDetails(_context.Messages)
                .FirstAsync(m => m.Id == message.Id, ct);

            return ChatService.MapMessageToDto(saved, senderId);
        }
        catch
        {
            await RollbackUploadedFilesAsync(uploadedKeys);
            throw;
        }
    }

    private async Task<(Conversation Conversation, ConversationMember Member)> EnsureCanSendAsync(
        Guid conversationId,
        Guid senderId,
        CancellationToken ct)
    {
        var member = await _context.ConversationMembers
            .Include(cm => cm.Conversation)
            .FirstOrDefaultAsync(cm => cm.ConversationId == conversationId && cm.UserId == senderId, ct);

        if (member == null)
            throw new UnauthorizedAccessException("Not a member of this conversation.");

        var conversation = member.Conversation;
        if (conversation.IsDisbanded)
            throw new InvalidOperationException("Cannot send messages to a disbanded group.");

        if (conversation.Type == ConversationType.Group
            && conversation.OnlyAdminsCanSend
            && member.Role is not (ConversationMemberRole.Owner or ConversationMemberRole.Admin))
        {
            throw new UnauthorizedAccessException("Only group owners and admins can send messages in this group.");
        }

        return (conversation, member);
    }

    private async Task<MessageAttachment> ProcessAndUploadImageAsync(
        Guid conversationId,
        SendMediaMessageItem item,
        FileValidationResult validation,
        int sortOrder,
        List<string> uploadedKeys,
        CancellationToken ct)
    {
        if (item.Content.CanSeek)
            item.Content.Position = 0;

        Stream uploadStream = item.Content;
        string uploadContentType = validation.NormalizedContentType ?? item.ContentType;
        string extension = validation.DetectedExtension ?? Path.GetExtension(item.FileName).ToLowerInvariant();
        long fileSizeBytes = item.Content.CanSeek ? item.Content.Length : 0;
        ProcessedImageResult? processedImage = null;

        try
        {
            if (_imageProcessingService != null)
            {
                processedImage = await _imageProcessingService.ProcessImageAsync(
                    item.Content,
                    _options.ImageMaxDimension,
                    _options.ImageQuality,
                    ct);
                uploadStream = processedImage.Stream;
                uploadContentType = processedImage.ContentType;
                extension = processedImage.Extension;
                if (processedImage.Stream.CanSeek)
                    fileSizeBytes = processedImage.Stream.Length;
            }

            var storageKey = $"chat/images/{conversationId}_{Guid.NewGuid():N}{extension}";
            uploadedKeys.Add(storageKey);

            await _mediaStorageService.UploadAsync(uploadStream, storageKey, uploadContentType, ct);

            var physicalPath = Path.Combine(_options.RootPath, storageKey);
            if (File.Exists(physicalPath))
                fileSizeBytes = new FileInfo(physicalPath).Length;

            return new MessageAttachment
            {
                AttachmentType = AttachmentType.Image,
                StorageUrl = _mediaStorageService.GetUrl(storageKey),
                MimeType = uploadContentType,
                OriginalFileName = TruncateFileName(item.FileName, 260),
                FileSizeBytes = fileSizeBytes,
                SortOrder = sortOrder
            };
        }
        finally
        {
            processedImage?.Stream.Dispose();
        }
    }

    private async Task<MessageAttachment> ProcessAndUploadVideoAsync(
        Guid conversationId,
        SendMediaMessageItem item,
        FileValidationResult validation,
        List<string> uploadedKeys,
        CancellationToken ct)
    {
        if (item.Content.CanSeek)
            item.Content.Position = 0;

        string uploadContentType = validation.NormalizedContentType ?? item.ContentType;
        string extension = validation.DetectedExtension ?? Path.GetExtension(item.FileName).ToLowerInvariant();
        long fileSizeBytes = item.Content.CanSeek ? item.Content.Length : 0;

        var storageKey = $"chat/videos/{conversationId}_{Guid.NewGuid():N}{extension}";
        uploadedKeys.Add(storageKey);

        await _mediaStorageService.UploadAsync(item.Content, storageKey, uploadContentType, ct);

        var physicalVideoPath = Path.Combine(_options.RootPath, storageKey);
        if (File.Exists(physicalVideoPath))
            fileSizeBytes = new FileInfo(physicalVideoPath).Length;

        string? thumbnailKey = null;
        double? durationSeconds = null;
        int? width = null;
        int? height = null;

        if (_mediaProcessingService != null && _mediaProcessingService.IsFFmpegAvailable && File.Exists(physicalVideoPath))
        {
            var inspection = await _mediaProcessingService.InspectVideoAsync(physicalVideoPath, ct);
            if (_options.RequireFFmpeg && !inspection.IsInspected)
                throw new InvalidOperationException("Cannot inspect this video. Please upload a valid MP4 or WEBM file.");

            var maxDuration = _options.VideoMaxDurationSeconds;
            if (inspection.IsInspected && inspection.DurationSeconds > maxDuration)
            {
                throw new InvalidOperationException(
                    $"Video duration ({inspection.DurationSeconds:F1}s) exceeds the maximum allowed duration of {maxDuration} seconds.");
            }

            if (inspection.IsInspected)
            {
                durationSeconds = inspection.DurationSeconds;
                width = inspection.Width;
                height = inspection.Height;

                var candidateKey = $"chat/videos/{conversationId}_{Guid.NewGuid():N}.mp4";
                var candidatePath = Path.Combine(_options.RootPath, candidateKey);
                uploadedKeys.Add(candidateKey);

                var transcoded = await _mediaProcessingService.TranscodeVideoAsync(physicalVideoPath, candidatePath, ct);
                bool replaced = false;

                if (transcoded != null
                    && File.Exists(candidatePath)
                    && new FileInfo(candidatePath).Length > 0
                    && new FileInfo(candidatePath).Length < new FileInfo(physicalVideoPath).Length)
                {
                    var verified = await _mediaProcessingService.InspectVideoAsync(candidatePath, ct);
                    if (verified.IsInspected
                        && verified.DurationSeconds.HasValue
                        && inspection.DurationSeconds.HasValue
                        && Math.Abs(verified.DurationSeconds.Value - inspection.DurationSeconds.Value) < 0.5)
                    {
                        await _mediaStorageService.DeleteAsync(storageKey, CancellationToken.None);
                        uploadedKeys.Remove(storageKey);

                        storageKey = candidateKey;
                        physicalVideoPath = candidatePath;
                        uploadContentType = "video/mp4";
                        fileSizeBytes = new FileInfo(candidatePath).Length;
                        durationSeconds = verified.DurationSeconds;
                        width = verified.Width ?? width;
                        height = verified.Height ?? height;
                        replaced = true;
                    }
                }

                if (!replaced)
                {
                    await _mediaStorageService.DeleteAsync(candidateKey, CancellationToken.None);
                    uploadedKeys.Remove(candidateKey);
                }

                var thumbCandidateKey = $"chat/thumbnails/{conversationId}_{Guid.NewGuid():N}.jpg";
                var thumbPath = Path.Combine(_options.RootPath, thumbCandidateKey);
                var thumb = await _mediaProcessingService.ExtractVideoThumbnailAsync(physicalVideoPath, thumbPath, ct);
                if (thumb != null && File.Exists(thumbPath))
                {
                    thumbnailKey = thumbCandidateKey;
                    uploadedKeys.Add(thumbnailKey);
                }
            }
        }

        return new MessageAttachment
        {
            AttachmentType = AttachmentType.Video,
            StorageUrl = _mediaStorageService.GetUrl(storageKey),
            ThumbnailUrl = thumbnailKey != null ? _mediaStorageService.GetUrl(thumbnailKey) : null,
            MimeType = uploadContentType,
            OriginalFileName = TruncateFileName(item.FileName, 260),
            FileSizeBytes = fileSizeBytes,
            DurationSeconds = durationSeconds,
            Width = width,
            Height = height,
            SortOrder = 0
        };
    }

    private async Task RollbackUploadedFilesAsync(List<string> uploadedKeys)
    {
        foreach (var key in uploadedKeys)
        {
            try
            {
                await _mediaStorageService.DeleteAsync(key, CancellationToken.None);
            }
            catch
            {
                // Best-effort cleanup so original error surfaces
            }
        }
    }

    private static string? SanitizeWaveformJson(string? waveformJson)
    {
        if (string.IsNullOrWhiteSpace(waveformJson))
            return null;

        List<double>? samples;
        try
        {
            samples = JsonSerializer.Deserialize<List<double>>(waveformJson);
        }
        catch (JsonException)
        {
            throw new ArgumentException("WaveformJson must be a valid JSON array of numbers.");
        }

        if (samples == null)
            throw new ArgumentException("WaveformJson must be a valid JSON array of numbers.");

        if (samples.Count == 0)
            return null;

        List<double> normalized;
        if (samples.Count <= MaxWaveformSamples)
        {
            normalized = samples.Select(ClampWaveformSample).ToList();
        }
        else
        {
            // Evenly downsample to MaxWaveformSamples preserving peak contour
            normalized = new List<double>(MaxWaveformSamples);
            double bucketSize = (double)samples.Count / MaxWaveformSamples;
            for (int i = 0; i < MaxWaveformSamples; i++)
            {
                int start = (int)Math.Floor(i * bucketSize);
                int end = Math.Min(samples.Count, (int)Math.Ceiling((i + 1) * bucketSize));
                double sum = 0;
                int count = 0;
                for (int j = start; j < end; j++)
                {
                    sum += ClampWaveformSample(samples[j]);
                    count++;
                }
                normalized.Add(count > 0 ? Math.Round(sum / count, 3) : 0);
            }
        }

        return JsonSerializer.Serialize(normalized);
    }

    private static double ClampWaveformSample(double value)
    {
        if (!double.IsFinite(value))
            return 0;

        return Math.Round(Math.Clamp(value, 0.0, 1.0), 3);
    }

    private static string? TruncateFileName(string? fileName, int maxLength)
    {
        if (string.IsNullOrWhiteSpace(fileName))
            return null;

        var safeName = Path.GetFileName(fileName);
        return safeName.Length <= maxLength ? safeName : safeName[..maxLength];
    }
}
