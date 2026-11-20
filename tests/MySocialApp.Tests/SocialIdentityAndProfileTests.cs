using System.Text.Json;
using MySocialApp.Application.DTOs.Comments;
using MySocialApp.Application.DTOs.Common;
using MySocialApp.Application.DTOs.Posts;
using MySocialApp.Application.Services;
using MySocialApp.Domain.Entities;
using MySocialApp.Domain.Enums;
using Xunit;

namespace MySocialApp.Tests;

public class SocialIdentityAndProfileTests
{
    [Fact]
    public async Task PublicProfile_ReturnsOnlySafeFields()
    {
        var context = TestDbContextFactory.Create(nameof(PublicProfile_ReturnsOnlySafeFields));
        var userA = new User
        {
            Id = Guid.NewGuid(),
            Username = "userA",
            Email = "userA@secret.com",
            PasswordHash = "SuperSecretHash123",
            DisplayName = "User A Display",
            AvatarUrl = "/media/avatars/userA.webp",
            Bio = "Hello world bio",
            Role = UserRole.User,
            IsActive = true
        };
        var userB = new User
        {
            Id = Guid.NewGuid(),
            Username = "userB",
            Email = "userB@secret.com",
            PasswordHash = "SuperSecretHash456",
            DisplayName = "User B Display",
            Role = UserRole.User,
            IsActive = true
        };

        context.Users.AddRange(userA, userB);
        context.Follows.Add(new Follow { FollowerId = userB.Id, FollowingId = userA.Id });
        context.Posts.Add(new Post { UserId = userA.Id, Content = "Post by A" });
        await context.SaveChangesAsync();

        var userService = new UserService(context);
        var profile = await userService.GetPublicProfileAsync(userA.Id, currentUserId: userB.Id);

        Assert.NotNull(profile);
        Assert.Equal(userA.Id, profile.Id);
        Assert.Equal("userA", profile.Username);
        Assert.Equal("User A Display", profile.DisplayName);
        Assert.Equal("/media/avatars/userA.webp", profile.AvatarUrl);
        Assert.Equal("Hello world bio", profile.Bio);
        Assert.Equal(1, profile.FollowerCount);
        Assert.Equal(0, profile.FollowingCount);
        Assert.Equal(1, profile.PostCount);
        Assert.True(profile.IsFollowing);
    }

    [Fact]
    public async Task PublicProfile_DoesNotExposeSensitiveUserData()
    {
        var context = TestDbContextFactory.Create(nameof(PublicProfile_DoesNotExposeSensitiveUserData));
        var user = new User
        {
            Id = Guid.NewGuid(),
            Username = "safeuser",
            Email = "private_email@example.com",
            PasswordHash = "SuperSecretHash",
            DisplayName = "Safe User",
            Role = UserRole.Admin,
            IsActive = true
        };
        context.Users.Add(user);
        await context.SaveChangesAsync();

        var userService = new UserService(context);
        var profile = await userService.GetPublicProfileAsync(user.Id);

        var json = JsonSerializer.Serialize(profile);

        Assert.DoesNotContain("private_email@example.com", json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("SuperSecretHash", json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("PasswordHash", json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Email", json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Role", json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("IsActive", json, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task FollowSelf_NotAllowed()
    {
        var context = TestDbContextFactory.Create(nameof(FollowSelf_NotAllowed));
        var userId = Guid.NewGuid();
        var user = new User
        {
            Id = userId,
            Username = "selfuser",
            Email = "self@example.com",
            PasswordHash = "pass",
            DisplayName = "Self User",
            IsActive = true
        };
        context.Users.Add(user);
        await context.SaveChangesAsync();

        var userService = new UserService(context);
        await Assert.ThrowsAsync<InvalidOperationException>(() => userService.FollowAsync(userId, userId));
    }

    [Fact]
    public async Task UserPosts_ReturnOnlyRequestedUserPosts()
    {
        var context = TestDbContextFactory.Create(nameof(UserPosts_ReturnOnlyRequestedUserPosts));
        var userA = new User { Id = Guid.NewGuid(), Username = "userA", Email = "a@a.com", PasswordHash = "p", DisplayName = "A", IsActive = true };
        var userB = new User { Id = Guid.NewGuid(), Username = "userB", Email = "b@b.com", PasswordHash = "p", DisplayName = "B", IsActive = true };

        context.Users.AddRange(userA, userB);
        context.Posts.Add(new Post { UserId = userA.Id, Content = "Post 1 from A" });
        context.Posts.Add(new Post { UserId = userA.Id, Content = "Post 2 from A" });
        context.Posts.Add(new Post { UserId = userB.Id, Content = "Post from B" });
        await context.SaveChangesAsync();

        var postService = new PostService(context);
        var result = await postService.GetUserPostsAsync(userA.Id, new PaginationParams { Page = 1, PageSize = 10 });

        Assert.Equal(2, result.TotalItems);
        Assert.All(result.Items, p => Assert.Equal(userA.Id, p.UserId));
    }

    [Fact]
    public async Task FeedPost_ReturnsAuthorSummary()
    {
        var context = TestDbContextFactory.Create(nameof(FeedPost_ReturnsAuthorSummary));
        var user = new User
        {
            Id = Guid.NewGuid(),
            Username = "author1",
            Email = "auth@test.com",
            PasswordHash = "p",
            DisplayName = "Author Display",
            AvatarUrl = "/media/avatars/auth.webp",
            IsActive = true
        };
        context.Users.Add(user);
        context.Posts.Add(new Post { UserId = user.Id, Content = "Feed post with author" });
        await context.SaveChangesAsync();

        var feedService = new FeedService(context);
        var feed = await feedService.GetSocialFeedAsync(user.Id, limit: 10);

        Assert.NotEmpty(feed.Items);
        var item = feed.Items.First();
        Assert.NotNull(item.Author);
        Assert.Equal(user.Id, item.Author.Id);
        Assert.Equal("author1", item.Author.Username);
        Assert.Equal("Author Display", item.Author.DisplayName);
        Assert.Equal("/media/avatars/auth.webp", item.Author.AvatarUrl);
    }

    [Fact]
    public async Task Comment_ReturnsAuthorSummary()
    {
        var context = TestDbContextFactory.Create(nameof(Comment_ReturnsAuthorSummary));
        var author = new User { Id = Guid.NewGuid(), Username = "postAuthor", Email = "pa@test.com", PasswordHash = "p", DisplayName = "Post Author", IsActive = true };
        var commenter = new User { Id = Guid.NewGuid(), Username = "commenter1", Email = "c1@test.com", PasswordHash = "p", DisplayName = "Commenter One", AvatarUrl = "/media/avatars/c1.webp", IsActive = true };

        context.Users.AddRange(author, commenter);
        var post = new Post { UserId = author.Id, Content = "Post for comments" };
        context.Posts.Add(post);
        await context.SaveChangesAsync();

        var commentService = new CommentService(context);
        await commentService.CreateAsync(post.Id, commenter.Id, new CreateCommentRequest { Content = "Great post!" });

        var comments = await commentService.GetByPostAsync(post.Id, new PaginationParams { Page = 1, PageSize = 10 });

        Assert.Equal(1, comments.TotalItems);
        var comment = comments.Items.First();
        Assert.Equal(commenter.Id, comment.UserId);
        Assert.Equal("commenter1", comment.Username);
        Assert.Equal("Commenter One", comment.DisplayName);
        Assert.Equal("/media/avatars/c1.webp", comment.UserAvatarUrl);
    }
}
