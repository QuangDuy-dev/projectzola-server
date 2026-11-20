using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using MySocialApp.Application.DTOs.Auth;
using MySocialApp.Application.DTOs.Chat;
using MySocialApp.Application.DTOs.Common;
using MySocialApp.Application.Interfaces;
using MySocialApp.Application.Options;
using MySocialApp.Application.Services;
using MySocialApp.Domain.Entities;
using MySocialApp.Domain.Enums;
using MySocialApp.Infrastructure.Data;
using MySocialApp.Infrastructure.Services;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;

namespace MySocialApp.Tests;

public class ChatMediaVoiceTests : IClassFixture<WebApplicationFactory<Program>>, IDisposable
{
    private readonly string _unitStorageDir;
    private readonly string _apiStorageDir;
    private readonly IOptions<MediaStorageOptions> _options;
    private readonly LocalMediaStorageService _storageService;
    private readonly FileValidator _fileValidator;
    private readonly ImageProcessingService _imageProcessingService;
    private readonly WebApplicationFactory<Program> _factory;

    public ChatMediaVoiceTests(WebApplicationFactory<Program> baseFactory)
    {
        // STRICT RULE: Use temporary directories, NEVER touch D:\Projectzola\storage in tests
        _unitStorageDir = Path.Combine(Path.GetTempPath(), "MySocialApp_ChatMediaUnit_" + Guid.NewGuid().ToString("N"));
        _apiStorageDir = Path.Combine(Path.GetTempPath(), "MySocialApp_ChatMediaApi_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_unitStorageDir);
        Directory.CreateDirectory(_apiStorageDir);

        _options = Microsoft.Extensions.Options.Options.Create(new MediaStorageOptions
        {
            RootPath = _unitStorageDir,
            BaseUrl = "/media",
            MaxImageSizeMB = 10,
            MaxVideoSizeMB = 200,
            MaxAudioSizeMB = 15,
            AudioMaxDurationSeconds = 300,
            MaxChatImagesPerMessage = 10
        });

        _storageService = new LocalMediaStorageService(_options, NullLogger<LocalMediaStorageService>.Instance);
        _fileValidator = new FileValidator(_options);
        _imageProcessingService = new ImageProcessingService(_options, NullLogger<ImageProcessingService>.Instance);

        _factory = baseFactory.WithWebHostBuilder(builder =>
        {
            builder.ConfigureAppConfiguration((_, config) =>
            {
                config.AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["MediaStorage:RootPath"] = _apiStorageDir,
                    ["MediaStorage:FFmpegPath"] = Path.Combine(_apiStorageDir, "missing-ffmpeg.exe"),
                    ["MediaStorage:FFprobePath"] = Path.Combine(_apiStorageDir, "missing-ffprobe.exe")
                });
            });
        });
    }

    public void Dispose()
    {
        _factory.Dispose();
        foreach (var dir in new[] { _unitStorageDir, _apiStorageDir })
        {
            try
            {
                if (Directory.Exists(dir))
                    Directory.Delete(dir, recursive: true);
            }
            catch
            {
                // Best-effort cleanup
            }
        }
    }

    // =========================================================================
    // HELPERS: Magic Byte Builders & Real Images
    // =========================================================================

    private static byte[] CreateValidWebmBytes(int size = 64)
    {
        var data = new byte[Math.Max(16, size)];
        data[0] = 0x1A; data[1] = 0x45; data[2] = 0xDF; data[3] = 0xA3; // EBML
        return data;
    }

    private static byte[] CreateValidOggBytes(int size = 64)
    {
        var data = new byte[Math.Max(16, size)];
        data[0] = 0x4F; data[1] = 0x67; data[2] = 0x67; data[3] = 0x53; // OggS
        return data;
    }

    private static byte[] CreateValidM4aBytes(int size = 64)
    {
        var data = new byte[Math.Max(16, size)];
        data[3] = 0x18;
        data[4] = 0x66; data[5] = 0x74; data[6] = 0x79; data[7] = 0x70; // ftyp
        return data;
    }

    private static byte[] CreateValidMp3Id3Bytes(int size = 64)
    {
        var data = new byte[Math.Max(16, size)];
        data[0] = 0x49; data[1] = 0x44; data[2] = 0x33; // ID3
        return data;
    }

    private static byte[] CreateValidMp3SyncBytes(int size = 64)
    {
        var data = new byte[Math.Max(16, size)];
        data[0] = 0xFF; data[1] = 0xFB; // MPEG Frame Sync (0xFF, (b1 & 0xE0) == 0xE0)
        return data;
    }

    private static byte[] CreateValidWavBytes(int size = 64)
    {
        var data = new byte[Math.Max(16, size)];
        data[0] = 0x52; data[1] = 0x49; data[2] = 0x46; data[3] = 0x46; // RIFF
        data[8] = 0x57; data[9] = 0x41; data[10] = 0x56; data[11] = 0x45; // WAVE
        return data;
    }

    private static byte[] CreateValidMp4Bytes(int size = 1024)
    {
        var data = new byte[Math.Max(16, size)];
        data[3] = 0x18;
        data[4] = 0x66; data[5] = 0x74; data[6] = 0x79; data[7] = 0x70; // ftyp
        return data;
    }

    private static byte[] CreateRealPngBytes(int width = 64, int height = 64)
    {
        using var img = new Image<Rgba32>(width, height);
        using var ms = new MemoryStream();
        img.SaveAsPng(ms);
        return ms.ToArray();
    }

    private static byte[] CreateRealJpegBytes(int width = 80, int height = 60)
    {
        using var img = new Image<Rgba32>(width, height);
        using var ms = new MemoryStream();
        img.SaveAsJpeg(ms);
        return ms.ToArray();
    }

    private static async Task<(User Owner, User Admin, User Member, User Outsider, Conversation Group)> SeedGroupAsync(
        MySocialAppDbContext context,
        bool onlyAdminsCanSend = false,
        bool isDisbanded = false)
    {
        var owner = new User { Id = Guid.NewGuid(), Username = $"o_{Guid.NewGuid():N}"[..12], Email = $"{Guid.NewGuid():N}@t.local", DisplayName = "Owner" };
        var admin = new User { Id = Guid.NewGuid(), Username = $"a_{Guid.NewGuid():N}"[..12], Email = $"{Guid.NewGuid():N}@t.local", DisplayName = "Admin" };
        var member = new User { Id = Guid.NewGuid(), Username = $"m_{Guid.NewGuid():N}"[..12], Email = $"{Guid.NewGuid():N}@t.local", DisplayName = "Member" };
        var outsider = new User { Id = Guid.NewGuid(), Username = $"x_{Guid.NewGuid():N}"[..12], Email = $"{Guid.NewGuid():N}@t.local", DisplayName = "Outsider" };

        var group = new Conversation
        {
            Id = Guid.NewGuid(),
            Type = ConversationType.Group,
            Name = "Media Group",
            CreatedById = owner.Id,
            OnlyAdminsCanSend = onlyAdminsCanSend,
            IsDisbanded = isDisbanded
        };
        group.Members.Add(new ConversationMember { ConversationId = group.Id, UserId = owner.Id, Role = ConversationMemberRole.Owner });
        group.Members.Add(new ConversationMember { ConversationId = group.Id, UserId = admin.Id, Role = ConversationMemberRole.Admin });
        group.Members.Add(new ConversationMember { ConversationId = group.Id, UserId = member.Id, Role = ConversationMemberRole.Member });

        context.Users.AddRange(owner, admin, member, outsider);
        context.Conversations.Add(group);
        await context.SaveChangesAsync();

        return (owner, admin, member, outsider, group);
    }

    private async Task<(Guid UserId, HttpClient Client)> CreateAuthenticatedApiUserAsync(string prefix)
    {
        var username = $"{prefix}_{Guid.NewGuid():N}"[..16];
        const string password = "Password123!";

        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<MySocialAppDbContext>();
            var hasher = new PasswordHasher<User>();
            var user = new User
            {
                Username = username,
                Email = $"{username}@test.local",
                DisplayName = $"Media {prefix}",
                Role = UserRole.User,
                IsActive = true,
                TokenVersion = 1
            };
            user.PasswordHash = hasher.HashPassword(user, password);
            db.Users.Add(user);
            await db.SaveChangesAsync();
        }

        var client = _factory.CreateClient();
        var loginRes = await client.PostAsJsonAsync("/api/auth/login", new LoginRequest
        {
            Username = username,
            Password = password
        });
        loginRes.EnsureSuccessStatusCode();
        var auth = (await loginRes.Content.ReadFromJsonAsync<ApiResponse<AuthResponse>>())!.Data!;
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", auth.AccessToken);
        return (auth.User.Id, client);
    }

    // =========================================================================
    // 1. FILE VALIDATOR: AUDIO MAGIC BYTES, CODECS PARAMETER & SIZE GUARD
    // =========================================================================

    [Fact]
    public void FileValidator_ValidAudioMagicBytes_AllSupportedFormatsPass()
    {
        var cases = new (byte[] Bytes, string FileName, string ContentType, string ExpectedExt, string ExpectedMime)[]
        {
            (CreateValidWebmBytes(), "note.webm", "audio/webm", ".webm", "audio/webm"),
            (CreateValidWebmBytes(), "opus.webm", "audio/webm;codecs=opus", ".webm", "audio/webm"),
            (CreateValidWebmBytes(), "recorder.webm", "video/webm;codecs=opus", ".webm", "audio/webm"),
            (CreateValidOggBytes(), "voice.ogg", "audio/ogg", ".ogg", "audio/ogg"),
            (CreateValidOggBytes(), "voice_opus.ogg", "audio/ogg;codecs=opus", ".ogg", "audio/ogg"),
            (CreateValidM4aBytes(), "memo.m4a", "audio/mp4", ".m4a", "audio/mp4"),
            (CreateValidM4aBytes(), "memo2.m4a", "audio/x-m4a", ".m4a", "audio/mp4"),
            (CreateValidMp3Id3Bytes(), "track_id3.mp3", "audio/mpeg", ".mp3", "audio/mpeg"),
            (CreateValidMp3SyncBytes(), "track_sync.mp3", "audio/mpeg", ".mp3", "audio/mpeg"),
            (CreateValidWavBytes(), "clip.wav", "audio/wav", ".wav", "audio/wav")
        };

        foreach (var (bytes, fileName, contentType, expectedExt, expectedMime) in cases)
        {
            using var stream = new MemoryStream(bytes);
            var result = _fileValidator.Validate(stream, fileName, contentType, AllowedMediaType.Audio);

            Assert.True(result.IsValid, $"Expected valid for {fileName} ({contentType}), got: {result.ErrorMessage}");
            Assert.True(result.IsAudio);
            Assert.False(result.IsVideo);
            Assert.Equal(expectedExt, result.DetectedExtension);
            Assert.Equal(expectedMime, result.NormalizedContentType);
        }
    }

    [Theory]
    [InlineData("spoofed.mp3", "audio/mpeg")]
    [InlineData("spoofed.wav", "audio/wav")]
    [InlineData("spoofed.webm", "audio/webm")]
    [InlineData("spoofed.ogg", "audio/ogg")]
    [InlineData("spoofed.m4a", "audio/mp4")]
    public void FileValidator_SpoofedAudioWithGarbageBytes_Rejected(string fileName, string contentType)
    {
        var garbage = new byte[] { 0x00, 0x11, 0x22, 0x33, 0x44, 0x55, 0x66, 0x77, 0x88, 0x99, 0xAA, 0xBB, 0xCC, 0xDD };
        using var stream = new MemoryStream(garbage);

        var result = _fileValidator.Validate(stream, fileName, contentType, AllowedMediaType.Audio);

        Assert.False(result.IsValid);
        Assert.Contains("invalid magic bytes", result.ErrorMessage);
    }

    [Fact]
    public void FileValidator_WavWithRiffWebpMagicBytes_Rejected()
    {
        // RIFF....WEBP pretending to be .wav
        var webpBytes = new byte[] { 0x52, 0x49, 0x46, 0x46, 0x10, 0x00, 0x00, 0x00, 0x57, 0x45, 0x42, 0x50, 0x00, 0x00 };
        using var stream = new MemoryStream(webpBytes);

        var result = _fileValidator.Validate(stream, "fake.wav", "audio/wav", AllowedMediaType.Audio);

        Assert.False(result.IsValid);
        Assert.Contains("invalid magic bytes", result.ErrorMessage);
    }

    [Fact]
    public void FileValidator_AudioExceeds15MB_Rejected_Within15MB_Accepted()
    {
        var overLimit = 15L * 1024 * 1024 + 1;
        using var overStream = new FakeSizedStream(new MemoryStream(CreateValidMp3Id3Bytes()), overLimit);
        var rejected = _fileValidator.Validate(overStream, "long.mp3", "audio/mpeg", AllowedMediaType.Audio);

        Assert.False(rejected.IsValid);
        Assert.Contains("exceeds the maximum allowed limit of 15 MB", rejected.ErrorMessage);

        var exactLimit = 15L * 1024 * 1024;
        using var okStream = new FakeSizedStream(new MemoryStream(CreateValidMp3Id3Bytes()), exactLimit);
        var accepted = _fileValidator.Validate(okStream, "exact15mb.mp3", "audio/mpeg", AllowedMediaType.Audio);

        Assert.True(accepted.IsValid);
        Assert.True(accepted.IsAudio);
    }

    // =========================================================================
    // 2. ChatMediaService.SendMediaMessageAsync TESTS
    // =========================================================================

    [Fact]
    public async Task SendMediaMessage_MultiImages_ConvertsPngAndJpegToWebpAttachments()
    {
        using var context = TestDbContextFactory.Create(nameof(SendMediaMessage_MultiImages_ConvertsPngAndJpegToWebpAttachments));
        var (owner, _, _, _, group) = await SeedGroupAsync(context);

        var service = new ChatMediaService(context, _storageService, _fileValidator, null, _imageProcessingService, _options);

        using var s1 = new MemoryStream(CreateRealPngBytes(120, 90));
        using var s2 = new MemoryStream(CreateRealJpegBytes(160, 120));
        using var s3 = new MemoryStream(CreateRealPngBytes(64, 64));

        var files = new List<SendMediaMessageItem>
        {
            new() { Content = s1, FileName = "pic1.png", ContentType = "image/png" },
            new() { Content = s2, FileName = "pic2.jpg", ContentType = "image/jpeg" },
            new() { Content = s3, FileName = "pic3.png", ContentType = "image/png" }
        };

        var result = await service.SendMediaMessageAsync(
            group.Id,
            owner.Id,
            files,
            caption: "Trip photos",
            clientMessageId: "multi-img-1");

        Assert.Equal("Image", result.Type);
        Assert.Equal("Trip photos", result.Content);
        Assert.Equal(3, result.Attachments.Count);

        for (int i = 0; i < 3; i++)
        {
            var att = result.Attachments[i];
            Assert.Equal("Image", att.AttachmentType);
            Assert.Equal("image/webp", att.MimeType);
            Assert.StartsWith("/media/chat/images/", att.StorageUrl);
            Assert.EndsWith(".webp", att.StorageUrl);
            Assert.Equal(i, att.SortOrder);
            Assert.True(att.FileSizeBytes > 0);

            var key = _storageService.GetKeyFromUrl(att.StorageUrl)!;
            Assert.True(await _storageService.ExistsAsync(key));
        }
    }

    [Fact]
    public async Task SendMediaMessage_SingleVideo_UploadsTranscodesAndExtractsThumbnail()
    {
        using var context = TestDbContextFactory.Create(nameof(SendMediaMessage_SingleVideo_UploadsTranscodesAndExtractsThumbnail));
        var (owner, _, _, _, group) = await SeedGroupAsync(context);

        var fakeProcessor = new FakeMediaProcessor(
            videoDurationSeconds: 12.5,
            transcodedVideoBytes: 400,
            audioDurationSeconds: 10,
            audioTranscodeSucceeds: true);

        var service = new ChatMediaService(context, _storageService, _fileValidator, fakeProcessor, _imageProcessingService, _options);

        using var videoStream = new MemoryStream(CreateValidMp4Bytes(2048));
        var result = await service.SendMediaMessageAsync(
            group.Id,
            owner.Id,
            new List<SendMediaMessageItem>
            {
                new() { Content = videoStream, FileName = "clip.mp4", ContentType = "video/mp4" }
            },
            caption: "Demo clip");

        Assert.Equal("Video", result.Type);
        var att = Assert.Single(result.Attachments);
        Assert.Equal("Video", att.AttachmentType);
        Assert.Equal("video/mp4", att.MimeType);
        Assert.StartsWith("/media/chat/videos/", att.StorageUrl);
        Assert.NotNull(att.ThumbnailUrl);
        Assert.StartsWith("/media/chat/thumbnails/", att.ThumbnailUrl);
        Assert.Equal(12.5, att.DurationSeconds);
        Assert.Equal(720, att.Width);
        Assert.Equal(1280, att.Height);
        Assert.Equal(400, att.FileSizeBytes); // Replaced with smaller transcoded file

        Assert.True(await _storageService.ExistsAsync(_storageService.GetKeyFromUrl(att.StorageUrl)!));
        Assert.True(await _storageService.ExistsAsync(_storageService.GetKeyFromUrl(att.ThumbnailUrl!)!));
    }

    [Fact]
    public async Task SendMediaMessage_RejectsOver10Images_MixedMedia_NonMember_OnlyAdmins_AndDisbanded()
    {
        using var context = TestDbContextFactory.Create(nameof(SendMediaMessage_RejectsOver10Images_MixedMedia_NonMember_OnlyAdmins_AndDisbanded));
        var (owner, admin, member, outsider, group) = await SeedGroupAsync(context, onlyAdminsCanSend: true);

        var service = new ChatMediaService(context, _storageService, _fileValidator, null, _imageProcessingService, _options);

        // 1. Rejects > 10 images
        var elevenItems = Enumerable.Range(0, 11)
            .Select(i => new SendMediaMessageItem
            {
                Content = new MemoryStream(CreateRealPngBytes(16, 16)),
                FileName = $"p{i}.png",
                ContentType = "image/png"
            })
            .ToList();
        try
        {
            await Assert.ThrowsAsync<ArgumentException>(() => service.SendMediaMessageAsync(group.Id, owner.Id, elevenItems));
        }
        finally
        {
            foreach (var item in elevenItems) item.Content.Dispose();
        }

        // 2. Rejects mixing video + image
        using (var imgStream = new MemoryStream(CreateRealPngBytes(16, 16)))
        using (var vidStream = new MemoryStream(CreateValidMp4Bytes()))
        {
            await Assert.ThrowsAsync<ArgumentException>(() =>
                service.SendMediaMessageAsync(group.Id, owner.Id, new List<SendMediaMessageItem>
                {
                    new() { Content = imgStream, FileName = "a.png", ContentType = "image/png" },
                    new() { Content = vidStream, FileName = "b.mp4", ContentType = "video/mp4" }
                }));
        }

        // 3. Rejects non-member (IDOR)
        using (var s = new MemoryStream(CreateRealPngBytes(16, 16)))
        {
            await Assert.ThrowsAsync<UnauthorizedAccessException>(() =>
                service.SendMediaMessageAsync(group.Id, outsider.Id, new List<SendMediaMessageItem>
                {
                    new() { Content = s, FileName = "idor.png", ContentType = "image/png" }
                }));
        }

        // 4. Enforces OnlyAdminsCanSend (Member rejected, Admin allowed)
        using (var sMem = new MemoryStream(CreateRealPngBytes(16, 16)))
        {
            await Assert.ThrowsAsync<UnauthorizedAccessException>(() =>
                service.SendMediaMessageAsync(group.Id, member.Id, new List<SendMediaMessageItem>
                {
                    new() { Content = sMem, FileName = "blocked.png", ContentType = "image/png" }
                }));
        }

        using (var sAdm = new MemoryStream(CreateRealPngBytes(16, 16)))
        {
            var admMsg = await service.SendMediaMessageAsync(group.Id, admin.Id, new List<SendMediaMessageItem>
            {
                new() { Content = sAdm, FileName = "allowed.png", ContentType = "image/png" }
            });
            Assert.Equal("Image", admMsg.Type);
        }

        // 5. Enforces !IsDisbanded
        group.IsDisbanded = true;
        await context.SaveChangesAsync();

        using (var sDis = new MemoryStream(CreateRealPngBytes(16, 16)))
        {
            await Assert.ThrowsAsync<InvalidOperationException>(() =>
                service.SendMediaMessageAsync(group.Id, owner.Id, new List<SendMediaMessageItem>
                {
                    new() { Content = sDis, FileName = "disbanded.png", ContentType = "image/png" }
                }));
        }
    }

    // =========================================================================
    // 3. ChatMediaService.SendVoiceMessageAsync TESTS
    // =========================================================================

    [Fact]
    public async Task SendVoiceMessage_SanitizesWaveform_AndHandlesFfmpegUnavailableFallbackGracefully()
    {
        using var context = TestDbContextFactory.Create(nameof(SendVoiceMessage_SanitizesWaveform_AndHandlesFfmpegUnavailableFallbackGracefully));
        var (owner, _, _, _, group) = await SeedGroupAsync(context);

        // Point MediaProcessingService to missing binaries -> IsFFmpegAvailable == false
        var missingFfmpegOptions = Microsoft.Extensions.Options.Options.Create(new MediaStorageOptions
        {
            RootPath = _unitStorageDir,
            BaseUrl = "/media",
            FFmpegPath = Path.Combine(_unitStorageDir, "no-ffmpeg.exe"),
            FFprobePath = Path.Combine(_unitStorageDir, "no-ffprobe.exe"),
            AudioMaxDurationSeconds = 300
        });
        var unavailableProcessor = new MediaProcessingService(NullLogger<MediaProcessingService>.Instance, missingFfmpegOptions);
        Assert.False(unavailableProcessor.IsFFmpegAvailable);

        var service = new ChatMediaService(context, _storageService, _fileValidator, unavailableProcessor, _imageProcessingService, _options);

        // Waveform with out-of-range values (< 0 and > 1)
        using var audioStream = new MemoryStream(CreateValidWebmBytes(256));
        var msg = await service.SendVoiceMessageAsync(group.Id, owner.Id, new SendVoiceMessageInput
        {
            Content = audioStream,
            FileName = "recording.webm",
            ContentType = "audio/webm;codecs=opus",
            DurationSeconds = 14.567,
            WaveformJson = "[-0.8, 0.0, 0.35, 1.0, 2.75]",
            ClientMessageId = "voice-1"
        });

        Assert.Equal("Voice", msg.Type);
        var att = Assert.Single(msg.Attachments);
        Assert.Equal("Audio", att.AttachmentType);
        Assert.Equal("audio/webm", att.MimeType);
        Assert.StartsWith("/media/chat/voices/", att.StorageUrl);
        Assert.EndsWith(".webm", att.StorageUrl); // Kept original raw file because FFmpeg unavailable
        Assert.Equal(14.57, att.DurationSeconds);

        var sanitizedSamples = JsonSerializer.Deserialize<List<double>>(att.WaveformJson!)!;
        Assert.Equal(new List<double> { 0, 0, 0.35, 1, 1 }, sanitizedSamples);
        Assert.True(await _storageService.ExistsAsync(_storageService.GetKeyFromUrl(att.StorageUrl)!));

        // Large waveform (> 128 samples) is downsampled to <= 128 and clamped 0..1
        var largeSamples = Enumerable.Range(0, 256).Select(i => i % 2 == 0 ? 1.5 : -0.2).ToList();
        using var audioStream2 = new MemoryStream(CreateValidOggBytes(128));
        var msg2 = await service.SendVoiceMessageAsync(group.Id, owner.Id, new SendVoiceMessageInput
        {
            Content = audioStream2,
            FileName = "longwave.ogg",
            ContentType = "audio/ogg",
            DurationSeconds = 8,
            WaveformJson = JsonSerializer.Serialize(largeSamples)
        });
        var downsampled = JsonSerializer.Deserialize<List<double>>(msg2.Attachments[0].WaveformJson!)!;
        Assert.Equal(128, downsampled.Count);
        Assert.All(downsampled, v => Assert.InRange(v, 0.0, 1.0));
    }

    [Fact]
    public async Task SendVoiceMessage_RejectsDurationOver300s_AndHandlesTranscodeSuccessOrFallback()
    {
        using var context = TestDbContextFactory.Create(nameof(SendVoiceMessage_RejectsDurationOver300s_AndHandlesTranscodeSuccessOrFallback));
        var (owner, _, _, _, group) = await SeedGroupAsync(context);

        var baseService = new ChatMediaService(context, _storageService, _fileValidator, null, _imageProcessingService, _options);

        // 1. Client-declared duration > 300s rejected
        using (var sOver = new MemoryStream(CreateValidMp3Id3Bytes()))
        {
            await Assert.ThrowsAsync<ArgumentException>(() =>
                baseService.SendVoiceMessageAsync(group.Id, owner.Id, new SendVoiceMessageInput
                {
                    Content = sOver,
                    FileName = "too_long.mp3",
                    ContentType = "audio/mpeg",
                    DurationSeconds = 301
                }));
        }

        // 2. FFprobe-inspected duration > 300s rejected AND uploaded file rolled back
        var overDurationProcessor = new FakeMediaProcessor(10, 500, audioDurationSeconds: 305.2, audioTranscodeSucceeds: true);
        var strictVoiceService = new ChatMediaService(context, _storageService, _fileValidator, overDurationProcessor, _imageProcessingService, _options);

        var voicesDir = Path.Combine(_unitStorageDir, "chat", "voices");
        var countBefore = Directory.Exists(voicesDir) ? Directory.GetFiles(voicesDir).Length : 0;

        using (var sInspectedOver = new MemoryStream(CreateValidWebmBytes()))
        {
            await Assert.ThrowsAsync<InvalidOperationException>(() =>
                strictVoiceService.SendVoiceMessageAsync(group.Id, owner.Id, new SendVoiceMessageInput
                {
                    Content = sInspectedOver,
                    FileName = "lied_duration.webm",
                    ContentType = "audio/webm",
                    DurationSeconds = 10
                }));
        }

        var countAfter = Directory.Exists(voicesDir) ? Directory.GetFiles(voicesDir).Length : 0;
        Assert.Equal(countBefore, countAfter); // Rolled back cleanly

        // 3. Transcode fails (returns null) -> falls back to original file without failing
        var failingTranscodeProcessor = new FakeMediaProcessor(10, 500, audioDurationSeconds: 22.4, audioTranscodeSucceeds: false);
        var fallbackService = new ChatMediaService(context, _storageService, _fileValidator, failingTranscodeProcessor, _imageProcessingService, _options);

        using (var sFallback = new MemoryStream(CreateValidOggBytes(200)))
        {
            var fallbackMsg = await fallbackService.SendVoiceMessageAsync(group.Id, owner.Id, new SendVoiceMessageInput
            {
                Content = sFallback,
                FileName = "fallback.ogg",
                ContentType = "audio/ogg",
                DurationSeconds = 20
            });
            var att = Assert.Single(fallbackMsg.Attachments);
            Assert.EndsWith(".ogg", att.StorageUrl);
            Assert.Equal("audio/ogg", att.MimeType);
            Assert.Equal(22.4, att.DurationSeconds);
            Assert.True(await _storageService.ExistsAsync(_storageService.GetKeyFromUrl(att.StorageUrl)!));
        }

        // 4. Transcode succeeds -> swaps to .mp3 and removes raw .webm
        var okTranscodeProcessor = new FakeMediaProcessor(10, 500, audioDurationSeconds: 18.0, audioTranscodeSucceeds: true);
        var mp3Service = new ChatMediaService(context, _storageService, _fileValidator, okTranscodeProcessor, _imageProcessingService, _options);

        using (var sTranscode = new MemoryStream(CreateValidWebmBytes(300)))
        {
            var mp3Msg = await mp3Service.SendVoiceMessageAsync(group.Id, owner.Id, new SendVoiceMessageInput
            {
                Content = sTranscode,
                FileName = "convert.webm",
                ContentType = "audio/webm",
                DurationSeconds = 18
            });
            var att = Assert.Single(mp3Msg.Attachments);
            Assert.EndsWith(".mp3", att.StorageUrl);
            Assert.Equal("audio/mpeg", att.MimeType);
            Assert.True(await _storageService.ExistsAsync(_storageService.GetKeyFromUrl(att.StorageUrl)!));
        }
    }

    [Fact]
    public async Task SendVoiceMessage_EnforcesIdorOnlyAdminsDisbanded_AndIdempotency()
    {
        using var context = TestDbContextFactory.Create(nameof(SendVoiceMessage_EnforcesIdorOnlyAdminsDisbanded_AndIdempotency));
        var (owner, _, member, outsider, group) = await SeedGroupAsync(context, onlyAdminsCanSend: true);

        var service = new ChatMediaService(context, _storageService, _fileValidator, null, _imageProcessingService, _options);

        // Non-member rejected (IDOR)
        using (var sOut = new MemoryStream(CreateValidWebmBytes()))
        {
            await Assert.ThrowsAsync<UnauthorizedAccessException>(() =>
                service.SendVoiceMessageAsync(group.Id, outsider.Id, new SendVoiceMessageInput
                {
                    Content = sOut,
                    FileName = "idor.webm",
                    ContentType = "audio/webm",
                    DurationSeconds = 5
                }));
        }

        // Member blocked by OnlyAdminsCanSend
        using (var sMem = new MemoryStream(CreateValidWebmBytes()))
        {
            await Assert.ThrowsAsync<UnauthorizedAccessException>(() =>
                service.SendVoiceMessageAsync(group.Id, member.Id, new SendVoiceMessageInput
                {
                    Content = sMem,
                    FileName = "blocked.webm",
                    ContentType = "audio/webm",
                    DurationSeconds = 5
                }));
        }

        // Owner sends with ClientMessageId -> retry returns identical message without duplicating
        var clientId = "voice-idem-" + Guid.NewGuid().ToString("N");
        MessageDto first;
        using (var s1 = new MemoryStream(CreateValidWebmBytes()))
        {
            first = await service.SendVoiceMessageAsync(group.Id, owner.Id, new SendVoiceMessageInput
            {
                Content = s1,
                FileName = "first.webm",
                ContentType = "audio/webm",
                DurationSeconds = 6,
                ClientMessageId = clientId
            });
        }

        using (var s2 = new MemoryStream(CreateValidWebmBytes()))
        {
            var retry = await service.SendVoiceMessageAsync(group.Id, owner.Id, new SendVoiceMessageInput
            {
                Content = s2,
                FileName = "retry.webm",
                ContentType = "audio/webm",
                DurationSeconds = 6,
                ClientMessageId = clientId
            });
            Assert.Equal(first.Id, retry.Id);
        }

        // Disbanded group blocks voice messages
        group.IsDisbanded = true;
        await context.SaveChangesAsync();

        using (var sDis = new MemoryStream(CreateValidWebmBytes()))
        {
            await Assert.ThrowsAsync<InvalidOperationException>(() =>
                service.SendVoiceMessageAsync(group.Id, owner.Id, new SendVoiceMessageInput
                {
                    Content = sDis,
                    FileName = "disbanded.webm",
                    ContentType = "audio/webm",
                    DurationSeconds = 5
                }));
        }
    }

    // =========================================================================
    // 4. END-TO-END HTTP MULTIPART INTEGRATION TESTS
    // =========================================================================

    [Fact]
    public async Task Api_SendMediaMessage_MultipartImages_ReturnsCreatedAndEnforcesGuards()
    {
        var userA = await CreateAuthenticatedApiUserAsync("meda");
        var userB = await CreateAuthenticatedApiUserAsync("medb");
        var outsider = await CreateAuthenticatedApiUserAsync("medx");
        using var clientA = userA.Client;
        using var clientB = userB.Client;
        using var clientOutsider = outsider.Client;

        var convRes = await clientA.PostAsJsonAsync("/api/conversations", new CreateConversationRequest
        {
            ParticipantId = userB.UserId
        });
        convRes.EnsureSuccessStatusCode();
        var conv = (await convRes.Content.ReadFromJsonAsync<ApiResponse<ConversationDto>>())!.Data!;

        // 1. Valid multi-image multipart upload
        using (var form = new MultipartFormDataContent())
        {
            var img1 = new ByteArrayContent(CreateRealPngBytes(100, 100));
            img1.Headers.ContentType = new MediaTypeHeaderValue("image/png");
            form.Add(img1, "files", "first.png");

            var img2 = new ByteArrayContent(CreateRealJpegBytes(120, 80));
            img2.Headers.ContentType = new MediaTypeHeaderValue("image/jpeg");
            form.Add(img2, "files", "second.jpg");

            form.Add(new StringContent("Holiday snaps"), "content");
            form.Add(new StringContent("http-media-" + Guid.NewGuid().ToString("N")), "clientMessageId");

            var uploadRes = await clientA.PostAsync($"/api/conversations/{conv.Id}/messages/media", form);
            Assert.Equal(HttpStatusCode.Created, uploadRes.StatusCode);

            var body = (await uploadRes.Content.ReadFromJsonAsync<ApiResponse<MessageDto>>())!.Data!;
            Assert.Equal("Image", body.Type);
            Assert.Equal("Holiday snaps", body.Content);
            Assert.Equal(2, body.Attachments.Count);
            Assert.All(body.Attachments, a =>
            {
                Assert.Equal("Image", a.AttachmentType);
                Assert.Equal("image/webp", a.MimeType);
                Assert.EndsWith(".webp", a.StorageUrl);
            });

            // Verify static media route serves the file from temp storage root
            var staticRes = await clientA.GetAsync(body.Attachments[0].StorageUrl);
            Assert.Equal(HttpStatusCode.OK, staticRes.StatusCode);
            Assert.True(Directory.GetFiles(Path.Combine(_apiStorageDir, "chat", "images")).Length >= 2);
        }

        // 2. Non-member upload rejected with 403 Forbidden
        using (var idorForm = new MultipartFormDataContent())
        {
            var img = new ByteArrayContent(CreateRealPngBytes(32, 32));
            img.Headers.ContentType = new MediaTypeHeaderValue("image/png");
            idorForm.Add(img, "files", "spy.png");

            var idorRes = await clientOutsider.PostAsync($"/api/conversations/{conv.Id}/messages/media", idorForm);
            Assert.Equal(HttpStatusCode.Forbidden, idorRes.StatusCode);
        }

        // 3. Mixed image + video rejected with 400 BadRequest
        using (var mixedForm = new MultipartFormDataContent())
        {
            var img = new ByteArrayContent(CreateRealPngBytes(32, 32));
            img.Headers.ContentType = new MediaTypeHeaderValue("image/png");
            mixedForm.Add(img, "files", "img.png");

            var vid = new ByteArrayContent(CreateValidMp4Bytes(256));
            vid.Headers.ContentType = new MediaTypeHeaderValue("video/mp4");
            mixedForm.Add(vid, "files", "clip.mp4");

            var mixedRes = await clientA.PostAsync($"/api/conversations/{conv.Id}/messages/media", mixedForm);
            Assert.Equal(HttpStatusCode.BadRequest, mixedRes.StatusCode);
        }
    }

    [Fact]
    public async Task Api_SendVoiceMessage_MultipartAudio_ReturnsCreatedAndRejectsInvalidInputs()
    {
        var userA = await CreateAuthenticatedApiUserAsync("voca");
        var userB = await CreateAuthenticatedApiUserAsync("vocb");
        using var clientA = userA.Client;
        using var clientB = userB.Client;

        var convRes = await clientA.PostAsJsonAsync("/api/conversations", new CreateConversationRequest
        {
            ParticipantId = userB.UserId
        });
        convRes.EnsureSuccessStatusCode();
        var conv = (await convRes.Content.ReadFromJsonAsync<ApiResponse<ConversationDto>>())!.Data!;

        // 1. Valid voice upload with audio/webm;codecs=opus + waveform sanitization
        using (var form = new MultipartFormDataContent())
        {
            var audio = new ByteArrayContent(CreateValidWebmBytes(512));
            audio.Headers.ContentType = MediaTypeHeaderValue.Parse("audio/webm;codecs=opus");
            form.Add(audio, "audio", "voice-note.webm");
            form.Add(new StringContent("15.5"), "durationSeconds");
            form.Add(new StringContent("[-0.2, 0.4, 1.9]"), "waveformJson");
            form.Add(new StringContent("http-voice-" + Guid.NewGuid().ToString("N")), "clientMessageId");

            var res = await clientA.PostAsync($"/api/conversations/{conv.Id}/messages/voice", form);
            Assert.Equal(HttpStatusCode.Created, res.StatusCode);

            var msg = (await res.Content.ReadFromJsonAsync<ApiResponse<MessageDto>>())!.Data!;
            Assert.Equal("Voice", msg.Type);
            var att = Assert.Single(msg.Attachments);
            Assert.Equal("Audio", att.AttachmentType);
            Assert.Equal(15.5, att.DurationSeconds);
            Assert.Equal("[0,0.4,1]", att.WaveformJson);

            var staticRes = await clientA.GetAsync(att.StorageUrl);
            Assert.Equal(HttpStatusCode.OK, staticRes.StatusCode);
        }

        // 2. Duration > 300s rejected with 400 BadRequest
        using (var badDurationForm = new MultipartFormDataContent())
        {
            var audio = new ByteArrayContent(CreateValidMp3Id3Bytes(128));
            audio.Headers.ContentType = new MediaTypeHeaderValue("audio/mpeg");
            badDurationForm.Add(audio, "audio", "long.mp3");
            badDurationForm.Add(new StringContent("305"), "durationSeconds");

            var badRes = await clientA.PostAsync($"/api/conversations/{conv.Id}/messages/voice", badDurationForm);
            Assert.Equal(HttpStatusCode.BadRequest, badRes.StatusCode);
        }

        // 3. Spoofed MP3 magic bytes rejected with 400 BadRequest
        using (var spoofForm = new MultipartFormDataContent())
        {
            var fakeAudio = new ByteArrayContent(new byte[] { 0x01, 0x02, 0x03, 0x04, 0x05, 0x06, 0x07, 0x08 });
            fakeAudio.Headers.ContentType = new MediaTypeHeaderValue("audio/mpeg");
            spoofForm.Add(fakeAudio, "audio", "spoof.mp3");
            spoofForm.Add(new StringContent("5"), "durationSeconds");

            var spoofRes = await clientA.PostAsync($"/api/conversations/{conv.Id}/messages/voice", spoofForm);
            Assert.Equal(HttpStatusCode.BadRequest, spoofRes.StatusCode);
        }
    }

    private sealed class FakeMediaProcessor(
        double videoDurationSeconds,
        int transcodedVideoBytes,
        double audioDurationSeconds,
        bool audioTranscodeSucceeds) : IMediaProcessingService
    {
        public bool IsFFmpegAvailable => true;

        public Task<VideoInspectionResult> InspectVideoAsync(string fullPhysicalPath, CancellationToken cancellationToken = default) =>
            Task.FromResult(new VideoInspectionResult(true, videoDurationSeconds, 720, 1280, null));

        public async Task<string?> TranscodeVideoAsync(string sourcePhysicalPath, string targetPhysicalPath, CancellationToken cancellationToken = default)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(targetPhysicalPath)!);
            await File.WriteAllBytesAsync(targetPhysicalPath, new byte[transcodedVideoBytes], cancellationToken);
            return targetPhysicalPath;
        }

        public async Task<string?> ExtractVideoThumbnailAsync(string fullPhysicalVideoPath, string outputThumbnailPath, CancellationToken cancellationToken = default)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(outputThumbnailPath)!);
            await File.WriteAllBytesAsync(outputThumbnailPath, new byte[] { 0xFF, 0xD8, 0xFF, 0xE0 }, cancellationToken);
            return outputThumbnailPath;
        }

        public Task<AudioInspectionResult> InspectAudioAsync(string filePath, CancellationToken ct = default) =>
            Task.FromResult(new AudioInspectionResult(true, audioDurationSeconds));

        public async Task<string?> TranscodeAudioToMp3Async(string inputPath, string outputPath, CancellationToken ct = default)
        {
            if (!audioTranscodeSucceeds)
                return null;

            Directory.CreateDirectory(Path.GetDirectoryName(outputPath)!);
            await File.WriteAllBytesAsync(outputPath, CreateValidMp3Id3Bytes(128), ct);
            return outputPath;
        }
    }
}
