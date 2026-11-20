using System.Security;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using MySocialApp.Application.DTOs.Posts;
using MySocialApp.Application.DTOs.Products;
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

public class MediaStorageTests : IDisposable
{
    private readonly string _testStorageDir;
    private readonly IOptions<MediaStorageOptions> _options;
    private readonly LocalMediaStorageService _storageService;
    private readonly FileValidator _fileValidator;

    public MediaStorageTests()
    {
        // STRICT RULE: Use temporary directory, NEVER touch D:\Projectzola\storage in tests
        _testStorageDir = Path.Combine(Path.GetTempPath(), "MySocialApp_Tests_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_testStorageDir);

        _options = Microsoft.Extensions.Options.Options.Create(new MediaStorageOptions
        {
            RootPath = _testStorageDir,
            BaseUrl = "/media",
            MaxImageSizeMB = 10,
            MaxVideoSizeMB = 200
        });

        _storageService = new LocalMediaStorageService(_options, NullLogger<LocalMediaStorageService>.Instance);
        _fileValidator = new FileValidator(_options);
    }

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(_testStorageDir))
            {
                Directory.Delete(_testStorageDir, recursive: true);
            }
        }
        catch
        {
            // Best-effort cleanup
        }
    }

    private static MemoryStream CreateValidJpegStream(int sizeBytes = 100)
    {
        var stream = new MemoryStream();
        // JPEG magic bytes: FF D8 FF
        stream.Write(new byte[] { 0xFF, 0xD8, 0xFF, 0xE0, 0x00, 0x10, 0x4A, 0x46 });
        var remaining = Math.Max(0, sizeBytes - 8);
        if (remaining > 0)
        {
            stream.Write(new byte[remaining]);
        }
        stream.Position = 0;
        return stream;
    }

    private static MemoryStream CreateValidPngStream(int sizeBytes = 100)
    {
        var stream = new MemoryStream();
        // PNG magic bytes: 89 50 4E 47
        stream.Write(new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A });
        var remaining = Math.Max(0, sizeBytes - 8);
        if (remaining > 0)
        {
            stream.Write(new byte[remaining]);
        }
        stream.Position = 0;
        return stream;
    }

    private static MemoryStream CreateValidMp4Stream(int sizeBytes = 100)
    {
        var stream = new MemoryStream();
        // MP4 magic bytes: byte 4-7 = "ftyp"
        stream.Write(new byte[] { 0x00, 0x00, 0x00, 0x18, 0x66, 0x74, 0x79, 0x70 });
        var remaining = Math.Max(0, sizeBytes - 8);
        if (remaining > 0)
        {
            stream.Write(new byte[remaining]);
        }
        stream.Position = 0;
        return stream;
    }

    // ==========================================
    // 1. FILE VALIDATOR TESTS
    // ==========================================

    [Fact]
    public void FileValidator_ValidJpeg_PassesValidation()
    {
        using var stream = CreateValidJpegStream();
        var result = _fileValidator.Validate(stream, "avatar.jpg", "image/jpeg", AllowedMediaType.Image);

        Assert.True(result.IsValid);
        Assert.Null(result.ErrorMessage);
        Assert.Equal(".jpg", result.DetectedExtension);
        Assert.False(result.IsVideo);
    }

    [Fact]
    public void FileValidator_ValidPng_PassesValidation()
    {
        using var stream = CreateValidPngStream();
        var result = _fileValidator.Validate(stream, "photo.png", "image/png", AllowedMediaType.Image);

        Assert.True(result.IsValid);
        Assert.Null(result.ErrorMessage);
        Assert.Equal(".png", result.DetectedExtension);
        Assert.False(result.IsVideo);
    }

    [Fact]
    public void FileValidator_ValidMp4_PassesValidation()
    {
        using var stream = CreateValidMp4Stream();
        var result = _fileValidator.Validate(stream, "clip.mp4", "video/mp4", AllowedMediaType.Video);

        Assert.True(result.IsValid);
        Assert.Null(result.ErrorMessage);
        Assert.Equal(".mp4", result.DetectedExtension);
        Assert.True(result.IsVideo);
    }

    [Theory]
    [InlineData("malware.exe")]
    [InlineData("script.sh")]
    [InlineData("cmd.bat")]
    [InlineData("library.dll")]
    [InlineData("code.js")]
    [InlineData("page.html")]
    [InlineData("hack.php")]
    public void FileValidator_DangerousExtension_Rejected(string fileName)
    {
        using var stream = new MemoryStream(new byte[] { 1, 2, 3, 4 });
        var result = _fileValidator.Validate(stream, fileName, "application/octet-stream", AllowedMediaType.Any);

        Assert.False(result.IsValid);
        Assert.Contains("strictly prohibited", result.ErrorMessage);
    }

    [Fact]
    public void FileValidator_MimeMismatch_Rejected()
    {
        using var stream = CreateValidJpegStream();
        // .jpg file declared as application/pdf
        var result = _fileValidator.Validate(stream, "test.jpg", "application/pdf", AllowedMediaType.Image);

        Assert.False(result.IsValid);
        Assert.Contains("does not match file extension", result.ErrorMessage);
    }

    [Fact]
    public void FileValidator_InvalidMagicBytes_Rejected()
    {
        using var stream = new MemoryStream(new byte[] { 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00 });
        // Extension says .png, but magic bytes are all zeros
        var result = _fileValidator.Validate(stream, "test.png", "image/png", AllowedMediaType.Image);

        Assert.False(result.IsValid);
        Assert.Contains("invalid magic bytes", result.ErrorMessage);
    }

    [Fact]
    public void FileValidator_ImageExceedsMaxSize_Rejected()
    {
        // Dummy stream reporting length > 10MB
        var largeSize = 11 * 1024 * 1024;
        var fakeStream = new FakeSizedStream(CreateValidJpegStream(), largeSize);

        var result = _fileValidator.Validate(fakeStream, "large.jpg", "image/jpeg", AllowedMediaType.Image);

        Assert.False(result.IsValid);
        Assert.Contains("exceeds the maximum allowed limit", result.ErrorMessage);
    }

    [Fact]
    public void FileValidator_VideoExceedsMaxSize_Rejected()
    {
        var largeSize = 201L * 1024 * 1024;
        var fakeStream = new FakeSizedStream(CreateValidMp4Stream(), largeSize);

        var result = _fileValidator.Validate(fakeStream, "large.mp4", "video/mp4", AllowedMediaType.Video);

        Assert.False(result.IsValid);
        Assert.Contains("exceeds the maximum allowed limit", result.ErrorMessage);
    }

    [Fact]
    public void FileValidator_EmptyStream_Rejected()
    {
        using var emptyStream = new MemoryStream();
        var result = _fileValidator.Validate(emptyStream, "empty.jpg", "image/jpeg", AllowedMediaType.Image);

        Assert.False(result.IsValid);
        Assert.Contains("empty", result.ErrorMessage);
    }

    // ==========================================
    // 2. LOCAL MEDIA STORAGE SERVICE TESTS
    // ==========================================

    [Fact]
    public async Task StorageService_UploadAndRead_Succeeds()
    {
        var key = "posts/images/test_img.jpg";
        using var stream = CreateValidJpegStream();

        var uploadedKey = await _storageService.UploadAsync(stream, key, "image/jpeg");
        Assert.Equal("posts/images/test_img.jpg", uploadedKey);

        var exists = await _storageService.ExistsAsync(key);
        Assert.True(exists);

        using var readStream = await _storageService.OpenReadAsync(key);
        Assert.NotNull(readStream);
        Assert.True(readStream.Length > 0);
    }

    [Fact]
    public async Task StorageService_Delete_RemovesFile()
    {
        var key = "avatars/to_delete.png";
        using var stream = CreateValidPngStream();
        await _storageService.UploadAsync(stream, key, "image/png");

        Assert.True(await _storageService.ExistsAsync(key));

        await _storageService.DeleteAsync(key);
        Assert.False(await _storageService.ExistsAsync(key));
    }

    [Fact]
    public async Task StorageService_DeleteNonExistent_DoesNotThrow()
    {
        var key = "avatars/non_existent.png";
        var exception = await Record.ExceptionAsync(() => _storageService.DeleteAsync(key));
        Assert.Null(exception);
    }

    [Theory]
    [InlineData("../../windows/system32/cmd.exe")]
    [InlineData("..\\..\\secret.txt")]
    [InlineData("/../../etc/passwd")]
    [InlineData("posts/../../evil.jpg")]
    public async Task StorageService_PathTraversal_ThrowsSecurityException(string maliciousKey)
    {
        using var stream = CreateValidJpegStream();
        await Assert.ThrowsAsync<SecurityException>(() => _storageService.UploadAsync(stream, maliciousKey, "image/jpeg"));
        await Assert.ThrowsAsync<SecurityException>(() => _storageService.ExistsAsync(maliciousKey));
    }

    [Fact]
    public void StorageService_GetUrlAndKeyFromUrl_AreConsistent()
    {
        var key = "posts/images/photo1.jpg";
        var url = _storageService.GetUrl(key);

        Assert.Equal("/media/posts/images/photo1.jpg", url);

        var extractedKey = _storageService.GetKeyFromUrl(url);
        Assert.Equal("posts/images/photo1.jpg", extractedKey);
    }

    // ==========================================
    // 3. USER AVATAR INTEGRATION TESTS
    // ==========================================

    [Fact]
    public async Task UserService_UpdateAvatar_SucceedsAndReplacesOldAvatar()
    {
        using var context = TestDbContextFactory.Create(nameof(UserService_UpdateAvatar_SucceedsAndReplacesOldAvatar));
        var user = new User { Id = Guid.NewGuid(), Username = "avatar_user", Email = "av@av.com", DisplayName = "Av" };
        context.Users.Add(user);
        await context.SaveChangesAsync();

        var userService = new UserService(context, _storageService, _fileValidator);

        // 1. Upload initial avatar
        using var stream1 = CreateValidJpegStream();
        var userDto1 = await userService.UpdateAvatarAsync(user.Id, stream1, "first.jpg", "image/jpeg");

        Assert.NotNull(userDto1.AvatarUrl);
        Assert.StartsWith("/media/avatars/", userDto1.AvatarUrl);

        var key1 = _storageService.GetKeyFromUrl(userDto1.AvatarUrl)!;
        Assert.True(await _storageService.ExistsAsync(key1));

        // 2. Upload second avatar -> should replace and delete first avatar file
        using var stream2 = CreateValidPngStream();
        var userDto2 = await userService.UpdateAvatarAsync(user.Id, stream2, "second.png", "image/png");

        Assert.NotNull(userDto2.AvatarUrl);
        Assert.StartsWith("/media/avatars/", userDto2.AvatarUrl);
        Assert.NotEqual(userDto1.AvatarUrl, userDto2.AvatarUrl);

        var key2 = _storageService.GetKeyFromUrl(userDto2.AvatarUrl)!;
        Assert.True(await _storageService.ExistsAsync(key2));
        Assert.False(await _storageService.ExistsAsync(key1)); // Old avatar deleted!
    }

    [Fact]
    public async Task UserService_UpdateAvatar_WithInvalidFile_ThrowsArgumentException()
    {
        using var context = TestDbContextFactory.Create(nameof(UserService_UpdateAvatar_WithInvalidFile_ThrowsArgumentException));
        var user = new User { Id = Guid.NewGuid(), Username = "u_invalid", Email = "i@i.com", DisplayName = "Invalid" };
        context.Users.Add(user);
        await context.SaveChangesAsync();

        var userService = new UserService(context, _storageService, _fileValidator);

        using var invalidStream = new MemoryStream(new byte[] { 1, 2, 3, 4 });
        await Assert.ThrowsAsync<ArgumentException>(() =>
            userService.UpdateAvatarAsync(user.Id, invalidStream, "virus.exe", "application/octet-stream"));
    }

    // ==========================================
    // 4. POST MEDIA INTEGRATION TESTS
    // ==========================================

    [Fact]
    public async Task PostService_AddImageAndVideoMedia_Succeeds()
    {
        using var context = TestDbContextFactory.Create(nameof(PostService_AddImageAndVideoMedia_Succeeds));
        var user = new User { Id = Guid.NewGuid(), Username = "author_media", Email = "am@am.com", DisplayName = "Author" };
        var post = new Post { Id = Guid.NewGuid(), UserId = user.Id, Content = "Media post" };
        context.Users.Add(user);
        context.Posts.Add(post);
        await context.SaveChangesAsync();

        var mediaProcessing = new MediaProcessingService(NullLogger<MediaProcessingService>.Instance);
        var postService = new PostService(context, _storageService, _fileValidator, mediaProcessing, _options);

        // Add image
        using var imgStream = CreateValidJpegStream();
        var imgResult = await postService.AddMediaAsync(post.Id, user.Id, imgStream, "photo.jpg", "image/jpeg");

        Assert.NotNull(imgResult);
        Assert.Equal("Image", imgResult.MediaType);
        Assert.StartsWith("/media/posts/images/", imgResult.StorageUrl);

        var imgKey = _storageService.GetKeyFromUrl(imgResult.StorageUrl)!;
        Assert.True(await _storageService.ExistsAsync(imgKey));

        // Add video
        using var vidStream = CreateValidMp4Stream();
        var vidResult = await postService.AddMediaAsync(post.Id, user.Id, vidStream, "video.mp4", "video/mp4");

        Assert.NotNull(vidResult);
        Assert.Equal("Video", vidResult.MediaType);
        Assert.StartsWith("/media/posts/videos/", vidResult.StorageUrl);

        var vidKey = _storageService.GetKeyFromUrl(vidResult.StorageUrl)!;
        Assert.True(await _storageService.ExistsAsync(vidKey));

        // Verify post media count in DB
        var savedPost = await postService.GetByIdAsync(post.Id, user.Id);
        Assert.Equal(2, savedPost.Media.Count);
    }

    [Fact]
    public async Task PostService_AddSecondVideo_ThrowsInvalidOperationException()
    {
        using var context = TestDbContextFactory.Create(nameof(PostService_AddSecondVideo_ThrowsInvalidOperationException));
        var user = new User { Id = Guid.NewGuid(), Username = "video_author", Email = "va@va.com", DisplayName = "VA" };
        var post = new Post { Id = Guid.NewGuid(), UserId = user.Id, Content = "Video test" };
        context.Users.Add(user);
        context.Posts.Add(post);
        await context.SaveChangesAsync();

        var mediaProcessing = new MediaProcessingService(NullLogger<MediaProcessingService>.Instance);
        var postService = new PostService(context, _storageService, _fileValidator, mediaProcessing, _options);

        // First video succeeds
        using var v1 = CreateValidMp4Stream();
        await postService.AddMediaAsync(post.Id, user.Id, v1, "v1.mp4", "video/mp4");

        // Second video fails
        using var v2 = CreateValidMp4Stream();
        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            postService.AddMediaAsync(post.Id, user.Id, v2, "v2.mp4", "video/mp4"));

        Assert.Contains("maximum of 1 video", ex.Message);
    }

    [Fact]
    public async Task PostService_AddMoreThan10Images_ThrowsInvalidOperationException()
    {
        using var context = TestDbContextFactory.Create(nameof(PostService_AddMoreThan10Images_ThrowsInvalidOperationException));
        var user = new User { Id = Guid.NewGuid(), Username = "img_author", Email = "ia@ia.com", DisplayName = "IA" };
        var post = new Post { Id = Guid.NewGuid(), UserId = user.Id, Content = "10 images test" };
        context.Users.Add(user);
        context.Posts.Add(post);
        await context.SaveChangesAsync();

        var mediaProcessing = new MediaProcessingService(NullLogger<MediaProcessingService>.Instance);
        var postService = new PostService(context, _storageService, _fileValidator, mediaProcessing, _options);

        for (int i = 0; i < 10; i++)
        {
            using var s = CreateValidJpegStream();
            await postService.AddMediaAsync(post.Id, user.Id, s, $"img{i}.jpg", "image/jpeg");
        }

        // 11th image fails
        using var s11 = CreateValidJpegStream();
        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            postService.AddMediaAsync(post.Id, user.Id, s11, "img11.jpg", "image/jpeg"));

        Assert.Contains("cannot contain more than 10 images", ex.Message);
    }

    [Fact]
    public async Task PostService_DeleteMedia_RemovesFileAndRecord()
    {
        using var context = TestDbContextFactory.Create(nameof(PostService_DeleteMedia_RemovesFileAndRecord));
        var user = new User { Id = Guid.NewGuid(), Username = "del_author", Email = "da@da.com", DisplayName = "DA" };
        var post = new Post { Id = Guid.NewGuid(), UserId = user.Id, Content = "Delete test" };
        context.Users.Add(user);
        context.Posts.Add(post);
        await context.SaveChangesAsync();

        var mediaProcessing = new MediaProcessingService(NullLogger<MediaProcessingService>.Instance);
        var postService = new PostService(context, _storageService, _fileValidator, mediaProcessing, _options);

        using var s = CreateValidJpegStream();
        var media = await postService.AddMediaAsync(post.Id, user.Id, s, "pic.jpg", "image/jpeg");
        var key = _storageService.GetKeyFromUrl(media.StorageUrl)!;

        Assert.True(await _storageService.ExistsAsync(key));

        // Delete media
        await postService.DeleteMediaAsync(post.Id, media.Id, user.Id);

        Assert.False(await _storageService.ExistsAsync(key));
        var updatedPost = await postService.GetByIdAsync(post.Id, user.Id);
        Assert.Empty(updatedPost.Media);
    }

    // ==========================================
    // 5. PRODUCT IMAGE INTEGRATION TESTS
    // ==========================================

    [Fact]
    public async Task ProductService_AddAndRemoveImages_Succeeds()
    {
        using var context = TestDbContextFactory.Create(nameof(ProductService_AddAndRemoveImages_Succeeds));
        var owner = new User { Id = Guid.NewGuid(), Username = "shopowner", Email = "so@so.com", DisplayName = "SO", Role = UserRole.ShopOwner };
        var otherUser = new User { Id = Guid.NewGuid(), Username = "otheruser", Email = "ou@ou.com", DisplayName = "OU" };
        var shop = new Shop { Id = Guid.NewGuid(), OwnerId = owner.Id, Name = "Shop 1" };
        var product = new Product { Id = Guid.NewGuid(), ShopId = shop.Id, Name = "Product 1", Price = 50, Stock = 10 };

        context.Users.AddRange(owner, otherUser);
        context.Shops.Add(shop);
        context.Products.Add(product);
        await context.SaveChangesAsync();

        var productService = new ProductService(context, _storageService, _fileValidator);

        // Other user cannot add image
        using var failStream = CreateValidJpegStream();
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() =>
            productService.AddImageAsync(product.Id, otherUser.Id, failStream, "test.jpg", "image/jpeg"));

        // Shop owner adds image
        using var okStream = CreateValidJpegStream();
        var addedImage = await productService.AddImageAsync(product.Id, owner.Id, okStream, "prod.jpg", "image/jpeg");

        Assert.NotNull(addedImage);
        Assert.StartsWith("/media/products/images/", addedImage.ImageUrl);

        var key = _storageService.GetKeyFromUrl(addedImage.ImageUrl)!;
        Assert.True(await _storageService.ExistsAsync(key));

        // Delete image
        await productService.DeleteImageAsync(product.Id, addedImage.Id, owner.Id);
        Assert.False(await _storageService.ExistsAsync(key));
    }

    // ==========================================
    // 6. COLLISION & COMPENSATION TESTS
    // ==========================================

    [Fact]
    public async Task CollisionPrevention_SameFileName_GeneratesDistinctKeys()
    {
        using var context = TestDbContextFactory.Create(nameof(CollisionPrevention_SameFileName_GeneratesDistinctKeys));
        var user = new User { Id = Guid.NewGuid(), Username = "user_collision", Email = "col@col.com", DisplayName = "Col" };
        var post = new Post { Id = Guid.NewGuid(), UserId = user.Id, Content = "Collision test" };
        context.Users.Add(user);
        context.Posts.Add(post);
        await context.SaveChangesAsync();

        var mediaProcessing = new MediaProcessingService(NullLogger<MediaProcessingService>.Instance);
        var postService = new PostService(context, _storageService, _fileValidator, mediaProcessing, _options);

        using var s1 = CreateValidJpegStream();
        using var s2 = CreateValidJpegStream();
        var m1 = await postService.AddMediaAsync(post.Id, user.Id, s1, "photo.jpg", "image/jpeg");
        var m2 = await postService.AddMediaAsync(post.Id, user.Id, s2, "photo.jpg", "image/jpeg");

        Assert.NotEqual(m1.StorageUrl, m2.StorageUrl);
        var k1 = _storageService.GetKeyFromUrl(m1.StorageUrl)!;
        var k2 = _storageService.GetKeyFromUrl(m2.StorageUrl)!;

        Assert.True(await _storageService.ExistsAsync(k1));
        Assert.True(await _storageService.ExistsAsync(k2));
    }

    [Fact]
    public async Task Compensation_DbFailure_CleansUpOrphanFile()
    {
        var options = new DbContextOptionsBuilder<MySocialAppDbContext>()
            .UseInMemoryDatabase(nameof(Compensation_DbFailure_CleansUpOrphanFile))
            .Options;

        using var failingContext = new FailingDbContext(options);
        var user = new User { Id = Guid.NewGuid(), Username = "user_fail", Email = "f@f.com", DisplayName = "Failing" };
        failingContext.Users.Add(user);
        await ((DbContext)failingContext).SaveChangesAsync();

        failingContext.ThrowOnSave = true;

        var userService = new UserService(failingContext, _storageService, _fileValidator);

        using var stream = CreateValidJpegStream();
        await Assert.ThrowsAsync<DbUpdateException>(() =>
            userService.UpdateAvatarAsync(user.Id, stream, "avatar.jpg", "image/jpeg"));

        var avatarFiles = Directory.GetFiles(Path.Combine(_testStorageDir, "avatars"));
        Assert.Empty(avatarFiles);
    }

    // ==========================================
    // 7. IMAGE PROCESSING & WEBP COMPRESSION TESTS
    // ==========================================

    private static MemoryStream CreateRealTestImage(int width, int height)
    {
        using var image = new Image<Rgba32>(width, height);
        var ms = new MemoryStream();
        image.SaveAsJpeg(ms);
        ms.Position = 0;
        return ms;
    }

    [Fact]
    public async Task ImageProcessing_ProcessAvatar_ResizesToMax512AndEncodesToWebp()
    {
        var imageProcessing = new ImageProcessingService(_options, NullLogger<ImageProcessingService>.Instance);
        using var rawImage = CreateRealTestImage(1024, 768);

        var result = await imageProcessing.ProcessAvatarAsync(rawImage);

        Assert.NotNull(result);
        Assert.Equal("image/webp", result.ContentType);
        Assert.Equal(".webp", result.Extension);
        Assert.True(result.Stream.Length > 0);

        using var loaded = await Image.LoadAsync(result.Stream);
        Assert.True(loaded.Width <= 512);
        Assert.True(loaded.Height <= 512);
        Assert.Equal(512, loaded.Width);
        Assert.Equal(384, loaded.Height);
    }

    [Fact]
    public async Task ImageProcessing_ProcessImage_ResizesLargeImageAndEncodesToWebp()
    {
        var imageProcessing = new ImageProcessingService(_options, NullLogger<ImageProcessingService>.Instance);
        using var rawImage = CreateRealTestImage(2400, 1600);

        var result = await imageProcessing.ProcessImageAsync(rawImage, maxDimension: 1920, quality: 80);

        Assert.NotNull(result);
        Assert.Equal("image/webp", result.ContentType);
        Assert.Equal(".webp", result.Extension);

        using var loaded = await Image.LoadAsync(result.Stream);
        Assert.True(loaded.Width <= 1920);
        Assert.True(loaded.Height <= 1920);
        Assert.Equal(1920, loaded.Width);
        Assert.Equal(1280, loaded.Height);
        Assert.Null(loaded.Metadata.ExifProfile);
    }

    [Fact]
    public async Task UserService_UpdateAvatar_WithImageProcessing_StoresWebpAvatar()
    {
        using var context = TestDbContextFactory.Create(nameof(UserService_UpdateAvatar_WithImageProcessing_StoresWebpAvatar));
        var user = new User { Id = Guid.NewGuid(), Username = "avatar_webp_user", Email = "w@w.com", DisplayName = "WebP" };
        context.Users.Add(user);
        await context.SaveChangesAsync();

        var imageProcessing = new ImageProcessingService(_options, NullLogger<ImageProcessingService>.Instance);
        var userService = new UserService(context, _storageService, _fileValidator, imageProcessing);

        using var stream = CreateRealTestImage(800, 800);
        var userDto = await userService.UpdateAvatarAsync(user.Id, stream, "my_avatar.jpg", "image/jpeg");

        Assert.NotNull(userDto.AvatarUrl);
        Assert.EndsWith(".webp", userDto.AvatarUrl);

        var key = _storageService.GetKeyFromUrl(userDto.AvatarUrl)!;
        Assert.True(await _storageService.ExistsAsync(key));
    }

    [Fact]
    public async Task PostService_RequireFFmpeg_WhenMissing_ThrowsInvalidOperationException()
    {
        var strictOptions = Microsoft.Extensions.Options.Options.Create(new MediaStorageOptions
        {
            RootPath = _testStorageDir,
            BaseUrl = "/media",
            RequireFFmpeg = true,
            FFmpegPath = Path.Combine(_testStorageDir, "missing-ffmpeg.exe"),
            FFprobePath = Path.Combine(_testStorageDir, "missing-ffprobe.exe")
        });

        using var context = TestDbContextFactory.Create(nameof(PostService_RequireFFmpeg_WhenMissing_ThrowsInvalidOperationException));
        var user = new User { Id = Guid.NewGuid(), Username = "strict_author", Email = "st@st.com", DisplayName = "ST" };
        var post = new Post { Id = Guid.NewGuid(), UserId = user.Id, Content = "Strict video post" };
        context.Users.Add(user);
        context.Posts.Add(post);
        await context.SaveChangesAsync();

        var mediaProcessing = new MediaProcessingService(NullLogger<MediaProcessingService>.Instance, strictOptions);
        var postService = new PostService(context, _storageService, _fileValidator, mediaProcessing, strictOptions);

        using var vidStream = CreateValidMp4Stream();
        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            postService.AddMediaAsync(post.Id, user.Id, vidStream, "test.mp4", "video/mp4"));

        Assert.Contains("FFmpeg/ffprobe is required", ex.Message);
    }
}

internal class FailingDbContext : MySocialAppDbContext
{
    public bool ThrowOnSave { get; set; }

    public FailingDbContext(DbContextOptions<MySocialAppDbContext> options) : base(options) { }

    public override Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
    {
        if (ThrowOnSave)
        {
            throw new DbUpdateException("Simulated database failure for compensation test.");
        }
        return base.SaveChangesAsync(cancellationToken);
    }
}

/// <summary>
/// Helper stream wrapper for simulating large file streams without allocating 200MB memory in unit tests.
/// </summary>
internal class FakeSizedStream : Stream
{
    private readonly Stream _inner;
    private readonly long _fakeLength;

    public FakeSizedStream(Stream inner, long fakeLength)
    {
        _inner = inner;
        _fakeLength = fakeLength;
    }

    public override bool CanRead => _inner.CanRead;
    public override bool CanSeek => true;
    public override bool CanWrite => _inner.CanWrite;
    public override long Length => _fakeLength;
    public override long Position
    {
        get => _inner.Position;
        set => _inner.Position = value;
    }

    public override void Flush() => _inner.Flush();
    public override int Read(byte[] buffer, int offset, int count) => _inner.Read(buffer, offset, count);
    public override long Seek(long offset, SeekOrigin origin) => _inner.Seek(offset, origin);
    public override void SetLength(long value) => _inner.SetLength(value);
    public override void Write(byte[] buffer, int offset, int count) => _inner.Write(buffer, offset, count);

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _inner.Dispose();
        }
        base.Dispose(disposing);
    }
}
