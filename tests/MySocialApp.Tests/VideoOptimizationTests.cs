using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using MySocialApp.Application.Interfaces;
using MySocialApp.Application.Options;
using MySocialApp.Application.Services;
using MySocialApp.Domain.Entities;
using MySocialApp.Infrastructure.Services;

namespace MySocialApp.Tests;

public class VideoOptimizationTests
{
    [Theory]
    [InlineData(200, true)]
    [InlineData(2000, false)]
    [InlineData(1000, false)]
    [InlineData(0, false)]
    public async Task OnlySmallerCompleteOutputReplacesOriginal(int candidateBytes, bool replace)
    {
        await CheckUpload(candidateBytes, replace);
    }

    [Fact]
    public async Task TruncatedCandidateIsDiscardedEvenWhenSmaller() => await CheckUpload(200, false, truncated: true);

    private static async Task CheckUpload(int bytes, bool replaced, bool truncated = false)
    {
        var root = Path.Combine(Path.GetTempPath(), "zola-video-test-" + Guid.NewGuid().ToString("N"));
        try
        {
            var options = Options.Create(new MediaStorageOptions { RootPath = root, RequireFFmpeg = true });
            var storage = new LocalMediaStorageService(options, NullLogger<LocalMediaStorageService>.Instance);
            using var db = TestDbContextFactory.Create(Guid.NewGuid().ToString());
            var user = new User { Username = "video-author", Email = "video@example.test", DisplayName = "Video" };
            var post = new Post { UserId = user.Id, Content = "Video compression regression" };
            db.Users.Add(user); db.Posts.Add(post); await db.SaveChangesAsync();
            var processing = new FakeVideoProcessor(bytes, truncated);
            var service = new PostService(db, storage, new FileValidator(options), processing, options);
            var original = new byte[1000];
            "ftyp"u8.CopyTo(original.AsSpan(4));
            using var source = new MemoryStream(original);
            var result = await service.AddMediaAsync(post.Id, user.Id, source, "test.mp4", "video/mp4");
            var videos = Directory.GetFiles(Path.Combine(root, "posts/videos"));
            Assert.Single(videos);
            Assert.Equal(replaced ? bytes : original.Length, new FileInfo(videos[0]).Length);
            if (!replaced) Assert.Equal(original, await File.ReadAllBytesAsync(videos[0]));
            Assert.NotNull(result.ThumbnailUrl);
            Assert.Single(Directory.GetFiles(Path.Combine(root, "thumbnails")));
        }
        finally { if (Directory.Exists(root)) Directory.Delete(root, true); }
    }

    [Fact]
    public void ExplicitToolPathsDoNotFallBackToUserPath()
    {
        var processor = new MediaProcessingService(NullLogger<MediaProcessingService>.Instance,
            Options.Create(new MediaStorageOptions { FFmpegPath = Path.Combine(Path.GetTempPath(), Guid.NewGuid()+".exe"), FFprobePath = Path.Combine(Path.GetTempPath(), Guid.NewGuid()+".exe") }));
        Assert.False(processor.IsFFmpegAvailable);
    }

    private sealed class FakeVideoProcessor(int bytes, bool truncated) : IMediaProcessingService
    {
        private string? target;
        public bool IsFFmpegAvailable => true;
        public Task<VideoInspectionResult> InspectVideoAsync(string path, CancellationToken token = default) =>
            Task.FromResult(new VideoInspectionResult(true, truncated && path == target ? 1 : 5, 360, 640, null));
        public async Task<string?> TranscodeVideoAsync(string source, string output, CancellationToken token = default)
        {
            target = output;
            await File.WriteAllBytesAsync(output, new byte[bytes], token);
            return output;
        }
        public async Task<string?> ExtractVideoThumbnailAsync(string source, string output, CancellationToken token = default)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(output)!);
            await File.WriteAllBytesAsync(output, [0xff, 0xd8, 0xff], token);
            return output;
        }
    }
}
