using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using MySocialApp.Application.Common;
using MySocialApp.Application.DTOs.Auth;
using MySocialApp.Application.DTOs.Common;
using MySocialApp.Application.DTOs.Feed;
using MySocialApp.Application.DTOs.Posts;
using MySocialApp.Application.Services;
using MySocialApp.Domain.Entities;
using MySocialApp.Domain.Enums;
using MySocialApp.Infrastructure.Data;

namespace MySocialApp.Tests;

public class FeedPaginationAndVideoTests : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly WebApplicationFactory<Program> _factory;
    private readonly HttpClient _client;

    public FeedPaginationAndVideoTests(WebApplicationFactory<Program> factory)
    {
        _factory = factory;
        _client = factory.CreateClient();
    }

    private static (MySocialAppDbContext Context, FeedService Service) CreateService(string dbName)
    {
        var context = TestDbContextFactory.Create(dbName);
        var service = new FeedService(context);
        return (context, service);
    }

    private static User CreateUser(string name) => new()
    {
        Id = Guid.NewGuid(),
        Username = name,
        Email = $"{name}@test.com",
        DisplayName = $"Display {name}",
        AvatarUrl = $"/media/avatars/{name}.webp",
        PasswordHash = "hash",
        Role = UserRole.User,
        IsActive = true
    };

    [Fact]
    public async Task SocialFeed_ReturnsFollowedUsersPostsAndOwnPosts()
    {
        var (context, service) = CreateService(Guid.NewGuid().ToString());
        var userA = CreateUser("userA");
        var userB = CreateUser("userB");
        var userC = CreateUser("userC"); // Not followed
        context.Users.AddRange(userA, userB, userC);

        // A follows B
        context.Follows.Add(new Follow { FollowerId = userA.Id, FollowingId = userB.Id });

        var postB1 = new Post { Id = Guid.NewGuid(), UserId = userB.Id, Content = "B Post 1", CreatedAt = DateTime.UtcNow.AddMinutes(-10) };
        var postB2 = new Post { Id = Guid.NewGuid(), UserId = userB.Id, Content = "B Post 2", CreatedAt = DateTime.UtcNow.AddMinutes(-5) };
        var postA1 = new Post { Id = Guid.NewGuid(), UserId = userA.Id, Content = "A Post 1", CreatedAt = DateTime.UtcNow.AddMinutes(-3) };
        var postC1 = new Post { Id = Guid.NewGuid(), UserId = userC.Id, Content = "C Post 1", CreatedAt = DateTime.UtcNow.AddMinutes(-1) };

        context.Posts.AddRange(postB1, postB2, postA1, postC1);
        await context.SaveChangesAsync();

        // Limit = 3 -> Should get A1, B2, B1 (followed + own), not C1 because followed posts fulfill limit
        var result = await service.GetSocialFeedAsync(userA.Id, limit: 3);

        Assert.Equal(3, result.Items.Count);
        Assert.Contains(result.Items, p => p.Id == postA1.Id);
        Assert.Contains(result.Items, p => p.Id == postB2.Id);
        Assert.Contains(result.Items, p => p.Id == postB1.Id);
        Assert.DoesNotContain(result.Items, p => p.Id == postC1.Id);
    }

    [Fact]
    public async Task SocialFeed_OrdersNewestFirst()
    {
        var (context, service) = CreateService(Guid.NewGuid().ToString());
        var userA = CreateUser("userA_order");
        var userB = CreateUser("userB_order");
        context.Users.AddRange(userA, userB);
        context.Follows.Add(new Follow { FollowerId = userA.Id, FollowingId = userB.Id });

        var now = DateTime.UtcNow;
        var p1 = new Post { Id = Guid.NewGuid(), UserId = userB.Id, Content = "Oldest", CreatedAt = now.AddHours(-3) };
        var p2 = new Post { Id = Guid.NewGuid(), UserId = userB.Id, Content = "Middle", CreatedAt = now.AddHours(-2) };
        var p3 = new Post { Id = Guid.NewGuid(), UserId = userB.Id, Content = "Newest", CreatedAt = now.AddHours(-1) };
        context.Posts.AddRange(p1, p2, p3);
        await context.SaveChangesAsync();

        var result = await service.GetSocialFeedAsync(userA.Id, limit: 10);

        Assert.Equal(3, result.Items.Count);
        Assert.Equal(p3.Id, result.Items[0].Id);
        Assert.Equal(p2.Id, result.Items[1].Id);
        Assert.Equal(p1.Id, result.Items[2].Id);
    }

    [Fact]
    public async Task SocialFeed_UsesFallbackWhenFollowFeedInsufficient()
    {
        var (context, service) = CreateService(Guid.NewGuid().ToString());
        var userA = CreateUser("userA_fallback");
        var userB = CreateUser("userB_fallback");
        var userC = CreateUser("userC_fallback"); // Not followed
        context.Users.AddRange(userA, userB, userC);

        context.Follows.Add(new Follow { FollowerId = userA.Id, FollowingId = userB.Id });

        // User B has only 1 post
        var postB = new Post { Id = Guid.NewGuid(), UserId = userB.Id, Content = "Followed post", CreatedAt = DateTime.UtcNow.AddHours(-2) };
        // User C has 2 public posts
        var postC1 = new Post { Id = Guid.NewGuid(), UserId = userC.Id, Content = "Public post 1", CreatedAt = DateTime.UtcNow.AddHours(-1) };
        var postC2 = new Post { Id = Guid.NewGuid(), UserId = userC.Id, Content = "Public post 2", CreatedAt = DateTime.UtcNow.AddMinutes(-30) };

        context.Posts.AddRange(postB, postC1, postC2);
        await context.SaveChangesAsync();

        // Limit = 3, but followed user only has 1 post -> Fallback supplements with C1, C2
        var result = await service.GetSocialFeedAsync(userA.Id, limit: 3);

        Assert.Equal(3, result.Items.Count);
        Assert.Contains(result.Items, p => p.Id == postB.Id);
        Assert.Contains(result.Items, p => p.Id == postC1.Id);
        Assert.Contains(result.Items, p => p.Id == postC2.Id);
    }

    [Fact]
    public async Task SocialFeed_FallbackDoesNotDuplicateFollowedPosts()
    {
        var (context, service) = CreateService(Guid.NewGuid().ToString());
        var userA = CreateUser("userA_nodup");
        var userB = CreateUser("userB_nodup");
        context.Users.AddRange(userA, userB);
        context.Follows.Add(new Follow { FollowerId = userA.Id, FollowingId = userB.Id });

        var postB = new Post { Id = Guid.NewGuid(), UserId = userB.Id, Content = "Post B", CreatedAt = DateTime.UtcNow };
        context.Posts.Add(postB);
        await context.SaveChangesAsync();

        var result = await service.GetSocialFeedAsync(userA.Id, limit: 10);

        Assert.Single(result.Items);
        Assert.Equal(postB.Id, result.Items[0].Id);
    }

    [Fact]
    public async Task SocialFeed_RespectsLimit()
    {
        var (context, service) = CreateService(Guid.NewGuid().ToString());
        var userA = CreateUser("userA_limit");
        var userB = CreateUser("userB_limit");
        context.Users.AddRange(userA, userB);
        context.Follows.Add(new Follow { FollowerId = userA.Id, FollowingId = userB.Id });

        for (int i = 0; i < 15; i++)
        {
            context.Posts.Add(new Post
            {
                Id = Guid.NewGuid(),
                UserId = userB.Id,
                Content = $"Post {i}",
                CreatedAt = DateTime.UtcNow.AddMinutes(-i)
            });
        }
        await context.SaveChangesAsync();

        var result = await service.GetSocialFeedAsync(userA.Id, limit: 5);

        Assert.Equal(5, result.Items.Count);
        Assert.True(result.HasMore);
        Assert.NotNull(result.NextCursor);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(51)]
    [InlineData(100)]
    public async Task SocialFeed_RejectsInvalidLimit(int limit)
    {
        var (_, service) = CreateService(Guid.NewGuid().ToString());
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => service.GetSocialFeedAsync(Guid.NewGuid(), limit: limit));
    }

    [Fact]
    public async Task SocialFeed_DoesNotReturnDeletedPosts()
    {
        var (context, service) = CreateService(Guid.NewGuid().ToString());
        var userA = CreateUser("userA_deleted");
        var userB = CreateUser("userB_deleted");
        context.Users.AddRange(userA, userB);
        context.Follows.Add(new Follow { FollowerId = userA.Id, FollowingId = userB.Id });

        var activePost = new Post { Id = Guid.NewGuid(), UserId = userB.Id, Content = "Active", IsDeleted = false, CreatedAt = DateTime.UtcNow };
        var deletedPost = new Post { Id = Guid.NewGuid(), UserId = userB.Id, Content = "Deleted", IsDeleted = true, CreatedAt = DateTime.UtcNow.AddMinutes(1) };
        context.Posts.AddRange(activePost, deletedPost);
        await context.SaveChangesAsync();

        var result = await service.GetSocialFeedAsync(userA.Id, limit: 10);

        Assert.Single(result.Items);
        Assert.Equal(activePost.Id, result.Items[0].Id);
    }

    [Fact]
    public async Task SocialFeed_ReturnsCorrectAuthorAndReactionDetails()
    {
        var (context, service) = CreateService(Guid.NewGuid().ToString());
        var userA = CreateUser("userA_details");
        var userB = CreateUser("userB_details");
        var userC = CreateUser("userC_details");
        context.Users.AddRange(userA, userB, userC);
        context.Follows.Add(new Follow { FollowerId = userA.Id, FollowingId = userB.Id });

        var post = new Post
        {
            Id = Guid.NewGuid(),
            UserId = userB.Id,
            Content = "Detailed Post",
            CreatedAt = DateTime.UtcNow
        };
        context.Posts.Add(post);

        // Media
        context.PostMedia.Add(new PostMedia
        {
            Id = Guid.NewGuid(),
            PostId = post.Id,
            MediaType = MediaType.Image,
            StorageUrl = "/media/posts/test.webp",
            ThumbnailUrl = null,
            CreatedAt = DateTime.UtcNow
        });

        // Reactions: A likes, C dislikes
        context.PostReactions.Add(new PostReaction
        {
            Id = Guid.NewGuid(),
            PostId = post.Id,
            UserId = userA.Id,
            ReactionType = ReactionType.Like
        });
        context.PostReactions.Add(new PostReaction
        {
            Id = Guid.NewGuid(),
            PostId = post.Id,
            UserId = userC.Id,
            ReactionType = ReactionType.Dislike
        });

        // Comments: 2 active, 1 deleted
        context.Comments.Add(new Comment { Id = Guid.NewGuid(), PostId = post.Id, UserId = userA.Id, Content = "C1", IsDeleted = false });
        context.Comments.Add(new Comment { Id = Guid.NewGuid(), PostId = post.Id, UserId = userC.Id, Content = "C2", IsDeleted = false });
        context.Comments.Add(new Comment { Id = Guid.NewGuid(), PostId = post.Id, UserId = userC.Id, Content = "C3", IsDeleted = true });

        await context.SaveChangesAsync();

        var result = await service.GetSocialFeedAsync(userA.Id, limit: 10);

        Assert.Single(result.Items);
        var item = result.Items[0];
        Assert.Equal("userB_details", item.Author.Username);
        Assert.Equal("Display userB_details", item.Author.DisplayName);
        Assert.True(item.IsFollowingAuthor);
        Assert.Equal(1, item.LikeCount);
        Assert.Equal(1, item.DislikeCount);
        Assert.Equal(2, item.CommentCount);
        Assert.Equal("Like", item.CurrentUserReaction);
        Assert.Single(item.Media);
        Assert.Equal("Image", item.Media[0].MediaType);
        Assert.Equal("/media/posts/test.webp", item.Media[0].Url);
    }

    [Fact]
    public async Task Cursor_FirstPageAndSecondPage_NoDuplicates()
    {
        var (context, service) = CreateService(Guid.NewGuid().ToString());
        var userA = CreateUser("userA_cursor");
        var userB = CreateUser("userB_cursor");
        context.Users.AddRange(userA, userB);
        context.Follows.Add(new Follow { FollowerId = userA.Id, FollowingId = userB.Id });

        var now = DateTime.UtcNow;
        var createdPosts = new List<Post>();
        for (int i = 0; i < 7; i++)
        {
            var p = new Post
            {
                Id = Guid.NewGuid(),
                UserId = userB.Id,
                Content = $"Cursor Post {i}",
                CreatedAt = now.AddMinutes(-i)
            };
            createdPosts.Add(p);
            context.Posts.Add(p);
        }
        await context.SaveChangesAsync();

        // Page 1: limit 3
        var page1 = await service.GetSocialFeedAsync(userA.Id, limit: 3);
        Assert.Equal(3, page1.Items.Count);
        Assert.True(page1.HasMore);
        Assert.NotNull(page1.NextCursor);

        // Page 2: limit 3 with cursor
        var page2 = await service.GetSocialFeedAsync(userA.Id, limit: 3, cursor: page1.NextCursor);
        Assert.Equal(3, page2.Items.Count);
        Assert.True(page2.HasMore);
        Assert.NotNull(page2.NextCursor);

        // Verify no duplicate IDs between Page 1 and Page 2
        var p1Ids = page1.Items.Select(x => x.Id).ToHashSet();
        var p2Ids = page2.Items.Select(x => x.Id).ToHashSet();
        Assert.Empty(p1Ids.Intersect(p2Ids));

        // Page 3: limit 3 with cursor -> remaining 1 post
        var page3 = await service.GetSocialFeedAsync(userA.Id, limit: 3, cursor: page2.NextCursor);
        Assert.Single(page3.Items);
        Assert.False(page3.HasMore);
        Assert.Null(page3.NextCursor);
    }

    [Fact]
    public async Task Cursor_SameCreatedAt_UsesIdTieBreaker()
    {
        var (context, service) = CreateService(Guid.NewGuid().ToString());
        var userA = CreateUser("userA_tie");
        var userB = CreateUser("userB_tie");
        context.Users.AddRange(userA, userB);
        context.Follows.Add(new Follow { FollowerId = userA.Id, FollowingId = userB.Id });

        var identicalTime = new DateTime(2026, 9, 21, 12, 0, 0, DateTimeKind.Utc);
        var p1 = new Post { Id = Guid.Parse("11111111-1111-1111-1111-111111111111"), UserId = userB.Id, Content = "Tie 1", CreatedAt = identicalTime };
        var p2 = new Post { Id = Guid.Parse("22222222-2222-2222-2222-222222222222"), UserId = userB.Id, Content = "Tie 2", CreatedAt = identicalTime };
        context.Posts.AddRange(p1, p2);
        await context.SaveChangesAsync();

        // Ordered by CreatedAt DESC, Id DESC: p2 (higher GUID) is first, p1 is second
        var page1 = await service.GetSocialFeedAsync(userA.Id, limit: 1);
        Assert.Single(page1.Items);
        Assert.Equal(p2.Id, page1.Items[0].Id);
        Assert.True(page1.HasMore);
        Assert.NotNull(page1.NextCursor);

        var page2 = await service.GetSocialFeedAsync(userA.Id, limit: 1, cursor: page1.NextCursor);
        Assert.Single(page2.Items);
        Assert.Equal(p1.Id, page2.Items[0].Id);
        Assert.False(page2.HasMore);
        Assert.Null(page2.NextCursor);
    }

    [Fact]
    public async Task Cursor_InvalidCursor_ThrowsArgumentException()
    {
        var (_, service) = CreateService(Guid.NewGuid().ToString());

        await Assert.ThrowsAsync<ArgumentException>(() => service.GetSocialFeedAsync(Guid.NewGuid(), limit: 10, cursor: "not_a_valid_cursor"));
        await Assert.ThrowsAsync<ArgumentException>(() => service.GetVideoFeedAsync(Guid.NewGuid(), limit: 10, cursor: "{}"));
    }

    [Fact]
    public async Task Cursor_NewPostInsertedAfterFirstPage_DoesNotDisruptNextPage()
    {
        var (context, service) = CreateService(Guid.NewGuid().ToString());
        var userA = CreateUser("userA_insert");
        var userB = CreateUser("userB_insert");
        context.Users.AddRange(userA, userB);
        context.Follows.Add(new Follow { FollowerId = userA.Id, FollowingId = userB.Id });

        var baseTime = DateTime.UtcNow.AddHours(-1);
        var p1 = new Post { Id = Guid.NewGuid(), UserId = userB.Id, Content = "Post 1", CreatedAt = baseTime.AddMinutes(10) };
        var p2 = new Post { Id = Guid.NewGuid(), UserId = userB.Id, Content = "Post 2", CreatedAt = baseTime.AddMinutes(5) };
        var p3 = new Post { Id = Guid.NewGuid(), UserId = userB.Id, Content = "Post 3", CreatedAt = baseTime };
        context.Posts.AddRange(p1, p2, p3);
        await context.SaveChangesAsync();

        // Get Page 1 with limit 1 -> returns p1
        var page1 = await service.GetSocialFeedAsync(userA.Id, limit: 1);
        Assert.Single(page1.Items);
        Assert.Equal(p1.Id, page1.Items[0].Id);

        // Now a brand new post arrives at current time (newer than p1)
        var brandNewPost = new Post { Id = Guid.NewGuid(), UserId = userB.Id, Content = "Brand New", CreatedAt = DateTime.UtcNow };
        context.Posts.Add(brandNewPost);
        await context.SaveChangesAsync();

        // User requests Page 2 using cursor from Page 1 -> Must return p2, unaffected by brandNewPost!
        var page2 = await service.GetSocialFeedAsync(userA.Id, limit: 1, cursor: page1.NextCursor);
        Assert.Single(page2.Items);
        Assert.Equal(p2.Id, page2.Items[0].Id);
    }

    [Fact]
    public async Task VideoFeed_ReturnsOnlyVideoPosts_ExcludesImageAndTextOnly()
    {
        var (context, service) = CreateService(Guid.NewGuid().ToString());
        var userA = CreateUser("userA_video");
        var userB = CreateUser("userB_video");
        context.Users.AddRange(userA, userB);

        // Text-only post
        var textPost = new Post { Id = Guid.NewGuid(), UserId = userB.Id, Content = "Text only", CreatedAt = DateTime.UtcNow.AddMinutes(-30) };

        // Image-only post
        var imagePost = new Post { Id = Guid.NewGuid(), UserId = userB.Id, Content = "Image only", CreatedAt = DateTime.UtcNow.AddMinutes(-20) };
        var imageMedia = new PostMedia { Id = Guid.NewGuid(), PostId = imagePost.Id, MediaType = MediaType.Image, StorageUrl = "/media/posts/pic.webp" };

        // Video post
        var videoPost = new Post { Id = Guid.NewGuid(), UserId = userB.Id, Content = "Short video #tiktok", CreatedAt = DateTime.UtcNow.AddMinutes(-10) };
        var videoMedia = new PostMedia
        {
            Id = Guid.NewGuid(),
            PostId = videoPost.Id,
            MediaType = MediaType.Video,
            StorageUrl = "/media/posts/vid.mp4",
            ThumbnailUrl = "/media/posts/vid_thumb.webp"
        };

        context.Posts.AddRange(textPost, imagePost, videoPost);
        context.PostMedia.AddRange(imageMedia, videoMedia);
        await context.SaveChangesAsync();

        var result = await service.GetVideoFeedAsync(userA.Id, limit: 10);

        Assert.Single(result.Items);
        var item = result.Items[0];
        Assert.Equal(videoPost.Id, item.Id);
        Assert.Equal("Short video #tiktok", item.Content);
        Assert.Single(item.Media);
        Assert.Equal("Video", item.Media[0].MediaType);
        Assert.Equal("/media/posts/vid.mp4", item.Media[0].Url);
        Assert.Equal("/media/posts/vid_thumb.webp", item.Media[0].ThumbnailUrl);
    }

    [Fact]
    public async Task VideoFeed_HandlesNullThumbnail()
    {
        var (context, service) = CreateService(Guid.NewGuid().ToString());
        var user = CreateUser("user_null_thumb");
        context.Users.Add(user);

        var videoPost = new Post { Id = Guid.NewGuid(), UserId = user.Id, Content = "No thumb video", CreatedAt = DateTime.UtcNow };
        var videoMedia = new PostMedia
        {
            Id = Guid.NewGuid(),
            PostId = videoPost.Id,
            MediaType = MediaType.Video,
            StorageUrl = "/media/posts/raw.mp4",
            ThumbnailUrl = null
        };
        context.Posts.Add(videoPost);
        context.PostMedia.Add(videoMedia);
        await context.SaveChangesAsync();

        var result = await service.GetVideoFeedAsync(user.Id, limit: 10);

        Assert.Single(result.Items);
        Assert.Null(result.Items[0].Media[0].ThumbnailUrl);
    }

    [Fact]
    public async Task Feed_RequiresAuthentication_ReturnsUnauthorized()
    {
        var response = await _client.GetAsync("/api/feed");
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);

        var videoResponse = await _client.GetAsync("/api/feed/videos");
        Assert.Equal(HttpStatusCode.Unauthorized, videoResponse.StatusCode);
    }

    [Fact]
    public async Task Feed_WithValidToken_ReturnsSuccessAndCursorPagination()
    {
        // Login as user1
        var loginResponse = await _client.PostAsJsonAsync("/api/auth/login", new LoginRequest
        {
            Username = "user1",
            Password = "User@123"
        });
        var loginBody = await loginResponse.Content.ReadFromJsonAsync<ApiResponse<AuthResponse>>();
        Assert.NotNull(loginBody?.Data?.AccessToken);

        using var request = new HttpRequestMessage(HttpMethod.Get, "/api/feed?limit=5");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", loginBody.Data.AccessToken);

        var response = await _client.SendAsync(request);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var body = await response.Content.ReadFromJsonAsync<ApiResponse<CursorPagedResult<FeedPostDto>>>();
        Assert.NotNull(body);
        Assert.True(body.Success);
        Assert.NotNull(body.Data);
        Assert.NotNull(body.Data.Items);
    }

    [Fact]
    public async Task Feed_InvalidLimit_ReturnsBadRequest()
    {
        var loginResponse = await _client.PostAsJsonAsync("/api/auth/login", new LoginRequest
        {
            Username = "user1",
            Password = "User@123"
        });
        var loginBody = await loginResponse.Content.ReadFromJsonAsync<ApiResponse<AuthResponse>>();
        Assert.NotNull(loginBody?.Data?.AccessToken);

        using var request = new HttpRequestMessage(HttpMethod.Get, "/api/feed?limit=0");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", loginBody.Data.AccessToken);

        var response = await _client.SendAsync(request);
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Feed_InvalidCursor_ReturnsBadRequest()
    {
        var loginResponse = await _client.PostAsJsonAsync("/api/auth/login", new LoginRequest
        {
            Username = "user1",
            Password = "User@123"
        });
        var loginBody = await loginResponse.Content.ReadFromJsonAsync<ApiResponse<AuthResponse>>();
        Assert.NotNull(loginBody?.Data?.AccessToken);

        using var request = new HttpRequestMessage(HttpMethod.Get, "/api/feed?cursor=invalid_not_base64!");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", loginBody.Data.AccessToken);

        var response = await _client.SendAsync(request);
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task TwoUserSimulation_SocialAndVideoFeed_Succeeds()
    {
        var (context, service) = CreateService(Guid.NewGuid().ToString());
        var userA = CreateUser("sim_userA");
        var userB = CreateUser("sim_userB");
        var userC = CreateUser("sim_userC");
        context.Users.AddRange(userA, userB, userC);

        // A follows B (A does not follow C)
        context.Follows.Add(new Follow { FollowerId = userA.Id, FollowingId = userB.Id });

        var now = DateTime.UtcNow;

        // User B creates: 1 text, 1 image, 1 short video post
        var postB1 = new Post { Id = Guid.NewGuid(), UserId = userB.Id, Content = "B Text Post", CreatedAt = now.AddMinutes(-30) };
        var postB2 = new Post { Id = Guid.NewGuid(), UserId = userB.Id, Content = "B Image Post", CreatedAt = now.AddMinutes(-20) };
        var postB3 = new Post { Id = Guid.NewGuid(), UserId = userB.Id, Content = "B Video Post", CreatedAt = now.AddMinutes(-10) };

        var mediaB2 = new PostMedia
        {
            Id = Guid.NewGuid(),
            PostId = postB2.Id,
            MediaType = MediaType.Image,
            StorageUrl = "/media/posts/photo.webp",
            ThumbnailUrl = null
        };
        var mediaB3 = new PostMedia
        {
            Id = Guid.NewGuid(),
            PostId = postB3.Id,
            MediaType = MediaType.Video,
            StorageUrl = "/media/posts/video.mp4",
            ThumbnailUrl = "/media/posts/video_thumb.webp"
        };

        // User C creates 1 public post
        var postC1 = new Post { Id = Guid.NewGuid(), UserId = userC.Id, Content = "C Public Post", CreatedAt = now.AddMinutes(-5) };

        context.Posts.AddRange(postB1, postB2, postB3, postC1);
        context.PostMedia.AddRange(mediaB2, mediaB3);
        await context.SaveChangesAsync();

        // Step 1: User A gets Social Feed with limit = 2
        var page1 = await service.GetSocialFeedAsync(userA.Id, limit: 2);
        Assert.Equal(2, page1.Items.Count);
        Assert.True(page1.HasMore);
        Assert.NotNull(page1.NextCursor);
        // Both items should be from followed User B
        Assert.All(page1.Items, item => Assert.Equal(userB.Id, item.Author.Id));

        // Step 2: User A fetches next page with nextCursor and limit = 2
        var page2 = await service.GetSocialFeedAsync(userA.Id, limit: 2, cursor: page1.NextCursor);
        Assert.NotEmpty(page2.Items);
        // Verify no duplicates between Page 1 and Page 2
        var page1Ids = page1.Items.Select(x => x.Id).ToHashSet();
        var page2Ids = page2.Items.Select(x => x.Id).ToHashSet();
        Assert.Empty(page1Ids.Intersect(page2Ids));
        // Contains remainder of B's posts and C's fallback post
        Assert.Contains(page2.Items, item => item.Id == postB1.Id);
        Assert.Contains(page2.Items, item => item.Id == postC1.Id);

        // Step 3: User A gets Short Video Feed
        var videoFeed = await service.GetVideoFeedAsync(userA.Id, limit: 10);
        Assert.Single(videoFeed.Items);
        var videoItem = videoFeed.Items[0];
        Assert.Equal(postB3.Id, videoItem.Id);
        Assert.Equal("B Video Post", videoItem.Content);
        Assert.Single(videoItem.Media);
        Assert.Equal("Video", videoItem.Media[0].MediaType);
        Assert.Equal("/media/posts/video.mp4", videoItem.Media[0].Url);
        Assert.Equal("/media/posts/video_thumb.webp", videoItem.Media[0].ThumbnailUrl);
    }

    [Fact]
    public async Task SwaggerEndpoint_IncludesFeedEndpoints()
    {
        var response = await _client.GetAsync("/swagger/v1/swagger.json");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var content = await response.Content.ReadAsStringAsync();

        Assert.Contains("/api/feed", content);
        Assert.Contains("/api/feed/videos", content);
    }
}
