using MySocialApp.Application.DTOs.Posts;
using MySocialApp.Application.Services;
using MySocialApp.Domain.Entities;

namespace MySocialApp.Tests;

public class PostServiceTests
{
    [Fact]
    public async Task CreatePost_ValidRequest_CreatesPostSuccessfully()
    {
        using var context = TestDbContextFactory.Create(nameof(CreatePost_ValidRequest_CreatesPostSuccessfully));
        var user = new User { Id = Guid.NewGuid(), Username = "author", Email = "a@a.com", DisplayName = "Author" };
        context.Users.Add(user);
        await context.SaveChangesAsync();

        var postService = new PostService(context);
        var post = await postService.CreateAsync(user.Id, new CreatePostRequest { Content = "Hello world post" });

        Assert.NotNull(post);
        Assert.Equal("Hello world post", post.Content);
        Assert.Equal(user.Id, post.UserId);
        Assert.Equal("author", post.Username);
    }

    [Fact]
    public async Task ToggleReaction_LikeThenLikeAgain_TogglesOff()
    {
        using var context = TestDbContextFactory.Create(nameof(ToggleReaction_LikeThenLikeAgain_TogglesOff));
        var user = new User { Id = Guid.NewGuid(), Username = "reactor", Email = "r@r.com", DisplayName = "Reactor" };
        var post = new Post { Id = Guid.NewGuid(), UserId = user.Id, Content = "A post to react" };
        context.Users.Add(user);
        context.Posts.Add(post);
        await context.SaveChangesAsync();

        var postService = new PostService(context);

        // 1. Like
        var reaction1 = await postService.ToggleReactionAsync(post.Id, user.Id, "Like");
        Assert.Equal("Like", reaction1);

        var fetchedPost = await postService.GetByIdAsync(post.Id, user.Id);
        Assert.Equal(1, fetchedPost.LikeCount);
        Assert.Equal("Like", fetchedPost.CurrentUserReaction);

        // 2. Like again -> should toggle off (remove)
        var reaction2 = await postService.ToggleReactionAsync(post.Id, user.Id, "Like");
        Assert.Null(reaction2);

        fetchedPost = await postService.GetByIdAsync(post.Id, user.Id);
        Assert.Equal(0, fetchedPost.LikeCount);
        Assert.Null(fetchedPost.CurrentUserReaction);
    }

    [Fact]
    public async Task ToggleReaction_LikeThenDislike_SwitchesReaction()
    {
        using var context = TestDbContextFactory.Create(nameof(ToggleReaction_LikeThenDislike_SwitchesReaction));
        var user = new User { Id = Guid.NewGuid(), Username = "switcher", Email = "s@s.com", DisplayName = "Switcher" };
        var post = new Post { Id = Guid.NewGuid(), UserId = user.Id, Content = "React post" };
        context.Users.Add(user);
        context.Posts.Add(post);
        await context.SaveChangesAsync();

        var postService = new PostService(context);

        // Like first
        await postService.ToggleReactionAsync(post.Id, user.Id, "Like");

        // Then switch to Dislike
        var reaction = await postService.ToggleReactionAsync(post.Id, user.Id, "Dislike");
        Assert.Equal("Dislike", reaction);

        var fetchedPost = await postService.GetByIdAsync(post.Id, user.Id);
        Assert.Equal(0, fetchedPost.LikeCount);
        Assert.Equal(1, fetchedPost.DislikeCount);
        Assert.Equal("Dislike", fetchedPost.CurrentUserReaction);
    }
}
