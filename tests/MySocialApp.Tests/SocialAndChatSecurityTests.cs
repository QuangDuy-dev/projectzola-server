using MySocialApp.Application.DTOs.Chat;
using MySocialApp.Application.DTOs.Comments;
using MySocialApp.Application.DTOs.Common;
using MySocialApp.Application.DTOs.Posts;
using MySocialApp.Application.DTOs.Products;
using MySocialApp.Application.Services;
using MySocialApp.Domain.Entities;
using MySocialApp.Domain.Enums;

namespace MySocialApp.Tests;

public class SocialAndChatSecurityTests
{
    // ==========================================
    // FOLLOW TESTS
    // ==========================================
    [Fact]
    public async Task Follow_CannotFollowSelf()
    {
        using var context = TestDbContextFactory.Create(nameof(Follow_CannotFollowSelf));
        var user = new User { Id = Guid.NewGuid(), Username = "self", Email = "self@test.com", DisplayName = "Self" };
        context.Users.Add(user);
        await context.SaveChangesAsync();

        var userService = new UserService(context);
        await Assert.ThrowsAsync<InvalidOperationException>(() => userService.FollowAsync(user.Id, user.Id));
    }

    [Fact]
    public async Task Follow_DuplicateFollowIsPrevented()
    {
        using var context = TestDbContextFactory.Create(nameof(Follow_DuplicateFollowIsPrevented));
        var u1 = new User { Id = Guid.NewGuid(), Username = "u1", Email = "u1@test.com", DisplayName = "User 1" };
        var u2 = new User { Id = Guid.NewGuid(), Username = "u2", Email = "u2@test.com", DisplayName = "User 2" };
        context.Users.AddRange(u1, u2);
        await context.SaveChangesAsync();

        var userService = new UserService(context);
        await userService.FollowAsync(u1.Id, u2.Id);

        // Second follow should throw InvalidOperationException
        await Assert.ThrowsAsync<InvalidOperationException>(() => userService.FollowAsync(u1.Id, u2.Id));
    }

    [Fact]
    public async Task Follow_UnfollowAndCountsWorkCorrectly()
    {
        using var context = TestDbContextFactory.Create(nameof(Follow_UnfollowAndCountsWorkCorrectly));
        var u1 = new User { Id = Guid.NewGuid(), Username = "follower", Email = "f@test.com", DisplayName = "Follower" };
        var u2 = new User { Id = Guid.NewGuid(), Username = "following", Email = "fg@test.com", DisplayName = "Following" };
        context.Users.AddRange(u1, u2);
        await context.SaveChangesAsync();

        var userService = new UserService(context);
        await userService.FollowAsync(u1.Id, u2.Id);

        var u2Profile = await userService.GetByIdAsync(u2.Id);
        Assert.Equal(1, u2Profile.FollowerCount);

        var u1Profile = await userService.GetByIdAsync(u1.Id);
        Assert.Equal(1, u1Profile.FollowingCount);

        // Unfollow
        await userService.UnfollowAsync(u1.Id, u2.Id);

        u2Profile = await userService.GetByIdAsync(u2.Id);
        Assert.Equal(0, u2Profile.FollowerCount);
    }

    // ==========================================
    // CHAT SECURITY TESTS
    // ==========================================
    [Fact]
    public async Task Chat_MemberCanReadAndSend_NonMemberRejected()
    {
        using var context = TestDbContextFactory.Create(nameof(Chat_MemberCanReadAndSend_NonMemberRejected));

        var alice = new User { Id = Guid.NewGuid(), Username = "alice", Email = "a@test.com", DisplayName = "Alice" };
        var bob = new User { Id = Guid.NewGuid(), Username = "bob", Email = "b@test.com", DisplayName = "Bob" };
        var eve = new User { Id = Guid.NewGuid(), Username = "eve", Email = "e@test.com", DisplayName = "Eve" };

        var conv = new Conversation { Id = Guid.NewGuid() };
        conv.Members.Add(new ConversationMember { ConversationId = conv.Id, UserId = alice.Id });
        conv.Members.Add(new ConversationMember { ConversationId = conv.Id, UserId = bob.Id });

        context.Users.AddRange(alice, bob, eve);
        context.Conversations.Add(conv);
        await context.SaveChangesAsync();

        var chatService = new ChatService(context);

        // Alice (member) can send message
        var msg = await chatService.SendMessageAsync(conv.Id, alice.Id, new SendMessageRequest { Content = "Hello Bob" });
        Assert.NotNull(msg);
        Assert.Equal("Hello Bob", msg.Content);

        // Bob (member) can read messages
        var bobMessages = await chatService.GetMessagesAsync(conv.Id, bob.Id, new PaginationParams());
        Assert.Single(bobMessages.Items);

        // Eve (non-member) cannot send message
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() =>
            chatService.SendMessageAsync(conv.Id, eve.Id, new SendMessageRequest { Content = "Spy message" }));

        // Eve (non-member) cannot read messages
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() =>
            chatService.GetMessagesAsync(conv.Id, eve.Id, new PaginationParams()));
    }

    // ==========================================
    // AUTHORIZATION / OWNERSHIP TESTS
    // ==========================================
    [Fact]
    public async Task Post_UserCannotEditAnotherUsersPost()
    {
        using var context = TestDbContextFactory.Create(nameof(Post_UserCannotEditAnotherUsersPost));
        var author = new User { Id = Guid.NewGuid(), Username = "author", Email = "au@test.com", DisplayName = "Author" };
        var hacker = new User { Id = Guid.NewGuid(), Username = "hacker", Email = "h@test.com", DisplayName = "Hacker" };
        var post = new Post { Id = Guid.NewGuid(), UserId = author.Id, Content = "Original post" };

        context.Users.AddRange(author, hacker);
        context.Posts.Add(post);
        await context.SaveChangesAsync();

        var postService = new PostService(context);
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() =>
            postService.UpdateAsync(post.Id, hacker.Id, new UpdatePostRequest { Content = "Hacked content" }));
    }

    [Fact]
    public async Task Comment_UserCannotDeleteAnotherUsersComment_UnlessAdmin()
    {
        using var context = TestDbContextFactory.Create(nameof(Comment_UserCannotDeleteAnotherUsersComment_UnlessAdmin));
        var postAuthor = new User { Id = Guid.NewGuid(), Username = "pa", Email = "pa@test.com", DisplayName = "Post Author" };
        var commenter = new User { Id = Guid.NewGuid(), Username = "cm", Email = "cm@test.com", DisplayName = "Commenter" };
        var stranger = new User { Id = Guid.NewGuid(), Username = "st", Email = "st@test.com", DisplayName = "Stranger" };
        var post = new Post { Id = Guid.NewGuid(), UserId = postAuthor.Id, Content = "Post" };
        var comment = new Comment { Id = Guid.NewGuid(), PostId = post.Id, UserId = commenter.Id, Content = "Great post!" };

        context.Users.AddRange(postAuthor, commenter, stranger);
        context.Posts.Add(post);
        context.Comments.Add(comment);
        await context.SaveChangesAsync();

        var commentService = new CommentService(context);

        // Stranger cannot delete comment
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() =>
            commentService.DeleteAsync(comment.Id, stranger.Id, isAdmin: false));

        // Admin can delete comment
        await commentService.DeleteAsync(comment.Id, stranger.Id, isAdmin: true);
        var deletedComment = await context.Comments.FindAsync(comment.Id);
        Assert.True(deletedComment!.IsDeleted);
    }

    [Fact]
    public async Task Product_ShopOwnerCannotEditAnotherShopOwnersProduct()
    {
        using var context = TestDbContextFactory.Create(nameof(Product_ShopOwnerCannotEditAnotherShopOwnersProduct));
        var owner1 = new User { Id = Guid.NewGuid(), Username = "o1", Email = "o1@test.com", DisplayName = "Owner 1", Role = UserRole.ShopOwner };
        var owner2 = new User { Id = Guid.NewGuid(), Username = "o2", Email = "o2@test.com", DisplayName = "Owner 2", Role = UserRole.ShopOwner };

        var shop1 = new Shop { Id = Guid.NewGuid(), OwnerId = owner1.Id, Name = "Shop 1" };
        var product = new Product { Id = Guid.NewGuid(), ShopId = shop1.Id, Name = "Product 1", Price = 50m, Stock = 10 };

        context.Users.AddRange(owner1, owner2);
        context.Shops.Add(shop1);
        context.Products.Add(product);
        await context.SaveChangesAsync();

        var productService = new ProductService(context);

        // Owner 2 cannot edit Owner 1's product
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() =>
            productService.UpdateAsync(product.Id, owner2.Id, new UpdateProductRequest { Name = "Modified Name" }));
    }

    // ==========================================
    // PAGINATION TESTS
    // ==========================================
    [Fact]
    public async Task Pagination_RespectsPageAndPageSize_AndCapsAt50()
    {
        using var context = TestDbContextFactory.Create(nameof(Pagination_RespectsPageAndPageSize_AndCapsAt50));
        var author = new User { Id = Guid.NewGuid(), Username = "author2", Email = "a2@test.com", DisplayName = "Author" };
        context.Users.Add(author);

        for (int i = 1; i <= 60; i++)
        {
            context.Posts.Add(new Post
            {
                Id = Guid.NewGuid(),
                UserId = author.Id,
                Content = $"Post #{i}",
                CreatedAt = DateTime.UtcNow.AddMinutes(i)
            });
        }
        await context.SaveChangesAsync();

        var postService = new PostService(context);

        // Test normal page 1 with page size 20
        var p1 = await postService.GetPostsAsync(new PaginationParams { Page = 1, PageSize = 20 });
        Assert.Equal(20, p1.Items.Count);
        Assert.Equal(60, p1.TotalItems);
        Assert.Equal(3, p1.TotalPages);
        Assert.Equal(1, p1.Page);

        // Test page size exceeding maximum 50 gets capped
        var paramsExceeded = new PaginationParams { Page = 1, PageSize = 100 };
        Assert.Equal(50, paramsExceeded.PageSize); // Capped at 50

        var pCapped = await postService.GetPostsAsync(paramsExceeded);
        Assert.Equal(50, pCapped.Items.Count);
    }
}
