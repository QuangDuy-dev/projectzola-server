using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Http.Connections;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.SignalR.Client;
using Microsoft.Extensions.Logging.Abstractions;
using MySocialApp.Application.DTOs.Auth;
using MySocialApp.Application.DTOs.Comments;
using MySocialApp.Application.DTOs.Common;
using MySocialApp.Application.DTOs.Notifications;
using MySocialApp.Application.DTOs.Orders;
using MySocialApp.Application.DTOs.Posts;
using MySocialApp.Application.DTOs.Users;
using MySocialApp.Application.Interfaces;
using MySocialApp.Application.Services;
using MySocialApp.Domain.Entities;
using MySocialApp.Domain.Enums;

namespace MySocialApp.Tests;

public class TestRealtimeNotificationSender : IRealtimeNotificationSender
{
    public List<(Guid UserId, NotificationDto Notification)> SentNotifications { get; } = new();
    public List<(Guid UserId, NotificationReadEventDto Event)> SentReadEvents { get; } = new();
    public List<(Guid UserId, int Count)> SentCounts { get; } = new();
    public bool ShouldThrow { get; set; } = false;

    public Task SendNotificationAsync(Guid userId, NotificationDto notification)
    {
        if (ShouldThrow) throw new Exception("Simulated SignalR failure");
        SentNotifications.Add((userId, notification));
        return Task.CompletedTask;
    }

    public Task SendNotificationReadAsync(Guid userId, NotificationReadEventDto eventData)
    {
        if (ShouldThrow) throw new Exception("Simulated SignalR failure");
        SentReadEvents.Add((userId, eventData));
        return Task.CompletedTask;
    }

    public Task SendUnreadCountAsync(Guid userId, int unreadCount)
    {
        if (ShouldThrow) throw new Exception("Simulated SignalR failure");
        SentCounts.Add((userId, unreadCount));
        return Task.CompletedTask;
    }
}

public class NotificationTests : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly WebApplicationFactory<Program> _factory;
    private readonly HttpClient _client;

    public NotificationTests(WebApplicationFactory<Program> factory)
    {
        _factory = factory;
        _client = factory.CreateClient();
    }

    // ==========================================
    // 1. CORE NOTIFICATION TESTS
    // ==========================================
    [Fact]
    public async Task Notification_UserCanRetrieveOwnNotifications_IsolatedFromOthers()
    {
        using var context = TestDbContextFactory.Create(nameof(Notification_UserCanRetrieveOwnNotifications_IsolatedFromOthers));
        var userA = new User { Id = Guid.NewGuid(), Username = "userA", DisplayName = "User A" };
        var userB = new User { Id = Guid.NewGuid(), Username = "userB", DisplayName = "User B" };
        context.Users.AddRange(userA, userB);

        var sender = new TestRealtimeNotificationSender();
        var notifService = new NotificationService(context, sender, NullLogger<NotificationService>.Instance);

        await notifService.CreateAsync(userA.Id, NotificationType.NewFollower, "Title A", "Content A");
        await notifService.CreateAsync(userB.Id, NotificationType.OrderConfirmed, "Title B", "Content B");

        var userANotifs = await notifService.GetByUserAsync(userA.Id, new PaginationParams());
        Assert.Single(userANotifs.Items);
        Assert.Equal("Title A", userANotifs.Items.First().Title);

        var userBNotifs = await notifService.GetByUserAsync(userB.Id, new PaginationParams());
        Assert.Single(userBNotifs.Items);
        Assert.Equal("Title B", userBNotifs.Items.First().Title);
    }

    [Fact]
    public async Task Notification_UserCannotMarkAnotherUsersNotificationRead()
    {
        using var context = TestDbContextFactory.Create(nameof(Notification_UserCannotMarkAnotherUsersNotificationRead));
        var userA = new User { Id = Guid.NewGuid(), Username = "userA", DisplayName = "User A" };
        var userB = new User { Id = Guid.NewGuid(), Username = "userB", DisplayName = "User B" };
        context.Users.AddRange(userA, userB);

        var sender = new TestRealtimeNotificationSender();
        var notifService = new NotificationService(context, sender, NullLogger<NotificationService>.Instance);

        var notifA = await notifService.CreateAsync(userA.Id, NotificationType.NewFollower, "Title A", "Content A");

        // User B attempts to mark User A's notification as read
        await Assert.ThrowsAsync<KeyNotFoundException>(() => notifService.MarkAsReadAsync(notifA.Id, userB.Id));
    }

    [Fact]
    public async Task Notification_MarkRead_WorksAndIsIdempotent()
    {
        using var context = TestDbContextFactory.Create(nameof(Notification_MarkRead_WorksAndIsIdempotent));
        var user = new User { Id = Guid.NewGuid(), Username = "user", DisplayName = "User" };
        context.Users.Add(user);

        var sender = new TestRealtimeNotificationSender();
        var notifService = new NotificationService(context, sender, NullLogger<NotificationService>.Instance);

        var notif = await notifService.CreateAsync(user.Id, NotificationType.NewFollower, "Followed", "Someone followed you");
        Assert.False(notif.IsRead);

        // First mark read
        var readResult1 = await notifService.MarkAsReadAsync(notif.Id, user.Id);
        Assert.True(readResult1.IsRead);
        Assert.NotNull(readResult1.ReadAt);

        // Second mark read (Idempotent - does not throw)
        var readResult2 = await notifService.MarkAsReadAsync(notif.Id, user.Id);
        Assert.True(readResult2.IsRead);
    }

    [Fact]
    public async Task Notification_MarkAllRead_AffectsOnlyCurrentUser()
    {
        using var context = TestDbContextFactory.Create(nameof(Notification_MarkAllRead_AffectsOnlyCurrentUser));
        var userA = new User { Id = Guid.NewGuid(), Username = "userA", DisplayName = "User A" };
        var userB = new User { Id = Guid.NewGuid(), Username = "userB", DisplayName = "User B" };
        context.Users.AddRange(userA, userB);

        var sender = new TestRealtimeNotificationSender();
        var notifService = new NotificationService(context, sender, NullLogger<NotificationService>.Instance);

        await notifService.CreateAsync(userA.Id, NotificationType.NewFollower, "A1", "C1");
        await notifService.CreateAsync(userA.Id, NotificationType.PostLiked, "A2", "C2");
        await notifService.CreateAsync(userB.Id, NotificationType.OrderConfirmed, "B1", "C1");

        // User A marks all as read
        await notifService.MarkAllAsReadAsync(userA.Id);

        var countA = await notifService.GetUnreadCountAsync(userA.Id);
        var countB = await notifService.GetUnreadCountAsync(userB.Id);

        Assert.Equal(0, countA);
        Assert.Equal(1, countB); // User B's notification remains unread!
    }

    [Fact]
    public async Task Notification_PaginationAndOrder_NewestFirst()
    {
        using var context = TestDbContextFactory.Create(nameof(Notification_PaginationAndOrder_NewestFirst));
        var user = new User { Id = Guid.NewGuid(), Username = "user", DisplayName = "User" };
        context.Users.Add(user);

        var sender = new TestRealtimeNotificationSender();
        var notifService = new NotificationService(context, sender, NullLogger<NotificationService>.Instance);

        var n1 = await notifService.CreateAsync(user.Id, NotificationType.NewFollower, "First", "1");
        // Simulate delay
        var n2 = await notifService.CreateAsync(user.Id, NotificationType.PostLiked, "Second", "2");

        var paged = await notifService.GetByUserAsync(user.Id, new PaginationParams { Page = 1, PageSize = 1 });
        Assert.Single(paged.Items);
        Assert.Equal("Second", paged.Items.First().Title);
        Assert.Equal(2, paged.TotalItems);
    }

    // ==========================================
    // 2. SOCIAL EVENT NOTIFICATIONS
    // ==========================================
    [Fact]
    public async Task Follow_CreatesNotificationForTarget_AndNotForActor()
    {
        using var context = TestDbContextFactory.Create(nameof(Follow_CreatesNotificationForTarget_AndNotForActor));
        var u1 = new User { Id = Guid.NewGuid(), Username = "actor", DisplayName = "Actor User" };
        var u2 = new User { Id = Guid.NewGuid(), Username = "target", DisplayName = "Target User" };
        context.Users.AddRange(u1, u2);
        await context.SaveChangesAsync();

        var sender = new TestRealtimeNotificationSender();
        var notifService = new NotificationService(context, sender, NullLogger<NotificationService>.Instance);
        var userService = new UserService(context, notificationService: notifService);

        await userService.FollowAsync(u1.Id, u2.Id);

        // Verify target received NewFollower notification
        var targetNotifs = await notifService.GetByUserAsync(u2.Id, new PaginationParams());
        Assert.Single(targetNotifs.Items);
        Assert.Equal(NotificationType.NewFollower.ToString(), targetNotifs.Items.First().Type);
        Assert.Equal(u1.Id, targetNotifs.Items.First().RelatedEntityId);

        // Verify actor did NOT receive notification
        var actorNotifs = await notifService.GetByUserAsync(u1.Id, new PaginationParams());
        Assert.Empty(actorNotifs.Items);
    }

    [Fact]
    public async Task Unfollow_DoesNotCreateNotification()
    {
        using var context = TestDbContextFactory.Create(nameof(Unfollow_DoesNotCreateNotification));
        var u1 = new User { Id = Guid.NewGuid(), Username = "actor", DisplayName = "Actor User" };
        var u2 = new User { Id = Guid.NewGuid(), Username = "target", DisplayName = "Target User" };
        context.Users.AddRange(u1, u2);
        context.Follows.Add(new Follow { FollowerId = u1.Id, FollowingId = u2.Id });
        await context.SaveChangesAsync();

        var sender = new TestRealtimeNotificationSender();
        var notifService = new NotificationService(context, sender, NullLogger<NotificationService>.Instance);
        var userService = new UserService(context, notificationService: notifService);

        await userService.UnfollowAsync(u1.Id, u2.Id);

        var notifs = await notifService.GetByUserAsync(u2.Id, new PaginationParams());
        Assert.Empty(notifs.Items);
    }

    [Fact]
    public async Task Like_CreatesNotificationForPostOwner_ExcludesDislikeAndSelfLike()
    {
        using var context = TestDbContextFactory.Create(nameof(Like_CreatesNotificationForPostOwner_ExcludesDislikeAndSelfLike));
        var author = new User { Id = Guid.NewGuid(), Username = "author", DisplayName = "Author" };
        var fan = new User { Id = Guid.NewGuid(), Username = "fan", DisplayName = "Fan" };
        context.Users.AddRange(author, fan);

        var post = new Post { Id = Guid.NewGuid(), UserId = author.Id, Content = "Great post" };
        context.Posts.Add(post);
        await context.SaveChangesAsync();

        var sender = new TestRealtimeNotificationSender();
        var notifService = new NotificationService(context, sender, NullLogger<NotificationService>.Instance);
        var postService = new PostService(context, notificationService: notifService);

        // Fan dislikes post -> Should NOT create notification
        await postService.ToggleReactionAsync(post.Id, fan.Id, "Dislike");
        Assert.Equal(0, await notifService.GetUnreadCountAsync(author.Id));

        // Fan switches to Like -> Should create notification
        await postService.ToggleReactionAsync(post.Id, fan.Id, "Like");
        var authorNotifs = await notifService.GetByUserAsync(author.Id, new PaginationParams());
        Assert.Single(authorNotifs.Items);
        Assert.Equal(NotificationType.PostLiked.ToString(), authorNotifs.Items.First().Type);
        Assert.Equal(post.Id, authorNotifs.Items.First().RelatedEntityId);

        // Author likes own post -> Should NOT create notification
        await postService.ToggleReactionAsync(post.Id, author.Id, "Like");
        Assert.Equal(1, await notifService.GetUnreadCountAsync(author.Id)); // Still only 1!
    }

    [Fact]
    public async Task LikeSpam_IsDeduplicated()
    {
        using var context = TestDbContextFactory.Create(nameof(LikeSpam_IsDeduplicated));
        var author = new User { Id = Guid.NewGuid(), Username = "author", DisplayName = "Author" };
        var fan = new User { Id = Guid.NewGuid(), Username = "fan", DisplayName = "Fan" };
        context.Users.AddRange(author, fan);

        var post = new Post { Id = Guid.NewGuid(), UserId = author.Id, Content = "Great post" };
        context.Posts.Add(post);
        await context.SaveChangesAsync();

        var sender = new TestRealtimeNotificationSender();
        var notifService = new NotificationService(context, sender, NullLogger<NotificationService>.Instance);
        var postService = new PostService(context, notificationService: notifService);

        // Repeated cycle: Like -> Unlike -> Like -> Unlike -> Like
        await postService.ToggleReactionAsync(post.Id, fan.Id, "Like");
        await postService.ToggleReactionAsync(post.Id, fan.Id, "Like"); // Unlike
        await postService.ToggleReactionAsync(post.Id, fan.Id, "Like"); // Like again
        await postService.ToggleReactionAsync(post.Id, fan.Id, "Like"); // Unlike
        await postService.ToggleReactionAsync(post.Id, fan.Id, "Like"); // Like again

        // Total notifications in DB for author should be exactly 1!
        var authorNotifs = await notifService.GetByUserAsync(author.Id, new PaginationParams());
        Assert.Single(authorNotifs.Items);
    }

    [Fact]
    public async Task Comment_CreatesNotificationForPostOwner_ExcludesSelfComment()
    {
        using var context = TestDbContextFactory.Create(nameof(Comment_CreatesNotificationForPostOwner_ExcludesSelfComment));
        var author = new User { Id = Guid.NewGuid(), Username = "author", DisplayName = "Author" };
        var commenter = new User { Id = Guid.NewGuid(), Username = "commenter", DisplayName = "Commenter" };
        context.Users.AddRange(author, commenter);

        var post = new Post { Id = Guid.NewGuid(), UserId = author.Id, Content = "Post" };
        context.Posts.Add(post);
        await context.SaveChangesAsync();

        var sender = new TestRealtimeNotificationSender();
        var notifService = new NotificationService(context, sender, NullLogger<NotificationService>.Instance);
        var commentService = new CommentService(context, notificationService: notifService);

        // Other user comments -> Creates notification
        await commentService.CreateAsync(post.Id, commenter.Id, new CreateCommentRequest { Content = "Nice post!" });
        var authorNotifs = await notifService.GetByUserAsync(author.Id, new PaginationParams());
        Assert.Single(authorNotifs.Items);
        Assert.Equal(NotificationType.PostCommented.ToString(), authorNotifs.Items.First().Type);

        // Author comments on own post -> Does NOT create notification
        await commentService.CreateAsync(post.Id, author.Id, new CreateCommentRequest { Content = "Thanks!" });
        Assert.Equal(1, await notifService.GetUnreadCountAsync(author.Id)); // Still 1
    }

    // ==========================================
    // 3. ORDER EVENT NOTIFICATIONS
    // ==========================================
    [Fact]
    public async Task Order_LifecycleNotifications_WorkCorrectly()
    {
        using var context = TestDbContextFactory.Create(nameof(Order_LifecycleNotifications_WorkCorrectly));
        var buyer = new User { Id = Guid.NewGuid(), Username = "buyer", DisplayName = "Buyer" };
        var shopOwner = new User { Id = Guid.NewGuid(), Username = "shopowner", DisplayName = "Shop Owner" };
        var shipper = new User { Id = Guid.NewGuid(), Username = "shipper", DisplayName = "Shipper" };
        context.Users.AddRange(buyer, shopOwner, shipper);

        var shop = new Shop { Id = Guid.NewGuid(), OwnerId = shopOwner.Id, Name = "Shop 1" };
        context.Shops.Add(shop);

        var product = new Product { Id = Guid.NewGuid(), ShopId = shop.Id, Name = "Phone", Price = 100, Stock = 10 };
        context.Products.Add(product);

        var order = new Order
        {
            Id = Guid.NewGuid(),
            BuyerId = buyer.Id,
            ShopId = shop.Id,
            Status = OrderStatus.Pending
        };
        order.Items.Add(new OrderItem { OrderId = order.Id, ProductId = product.Id, Quantity = 1, UnitPrice = 100 });
        context.Orders.Add(order);
        await context.SaveChangesAsync();

        var sender = new TestRealtimeNotificationSender();
        var notifService = new NotificationService(context, sender, NullLogger<NotificationService>.Instance);
        var orderService = new OrderService(context, notificationService: notifService);

        // 1. Confirm -> Notifies Buyer
        await orderService.ConfirmAsync(order.Id, shopOwner.Id);
        var notifs1 = await notifService.GetByUserAsync(buyer.Id, new PaginationParams());
        Assert.Contains(notifs1.Items, n => n.Type == NotificationType.OrderConfirmed.ToString());

        // 2. Prepare -> Notifies Buyer
        await orderService.PrepareAsync(order.Id, shopOwner.Id);
        var notifs2 = await notifService.GetByUserAsync(buyer.Id, new PaginationParams());
        Assert.Contains(notifs2.Items, n => n.Type == NotificationType.OrderPreparing.ToString());

        // 3. ReadyForPickup -> Notifies Buyer
        await orderService.ReadyForPickupAsync(order.Id, shopOwner.Id);
        var notifs3 = await notifService.GetByUserAsync(buyer.Id, new PaginationParams());
        Assert.Contains(notifs3.Items, n => n.Type == NotificationType.OrderReadyForPickup.ToString());

        // 4. Claim -> Notifies Buyer and ShopOwner
        await orderService.ClaimAsync(order.Id, shipper.Id);
        var notifs4Buyer = await notifService.GetByUserAsync(buyer.Id, new PaginationParams());
        Assert.Contains(notifs4Buyer.Items, n => n.Type == NotificationType.OrderClaimed.ToString());
        var notifs4Shop = await notifService.GetByUserAsync(shopOwner.Id, new PaginationParams());
        Assert.Contains(notifs4Shop.Items, n => n.Type == NotificationType.OrderClaimed.ToString());

        // 5. Pickup (Shipping) -> Notifies Buyer
        await orderService.PickupAsync(order.Id, shipper.Id);
        var notifs5Buyer = await notifService.GetByUserAsync(buyer.Id, new PaginationParams());
        Assert.Contains(notifs5Buyer.Items, n => n.Type == NotificationType.OrderShipping.ToString());

        // 6. Deliver -> Notifies Buyer and ShopOwner
        await orderService.DeliverAsync(order.Id, shipper.Id);
        var notifs6Buyer = await notifService.GetByUserAsync(buyer.Id, new PaginationParams());
        Assert.Contains(notifs6Buyer.Items, n => n.Type == NotificationType.OrderDelivered.ToString());
        var notifs6Shop = await notifService.GetByUserAsync(shopOwner.Id, new PaginationParams());
        Assert.Contains(notifs6Shop.Items, n => n.Type == NotificationType.OrderDelivered.ToString());
    }

    [Fact]
    public async Task Order_CancellationNotifications_WorkBothWays()
    {
        using var context = TestDbContextFactory.Create(nameof(Order_CancellationNotifications_WorkBothWays));
        var buyer = new User { Id = Guid.NewGuid(), Username = "buyer", DisplayName = "Buyer" };
        var shopOwner = new User { Id = Guid.NewGuid(), Username = "shopowner", DisplayName = "Shop Owner" };
        context.Users.AddRange(buyer, shopOwner);

        var shop = new Shop { Id = Guid.NewGuid(), OwnerId = shopOwner.Id, Name = "Shop" };
        context.Shops.Add(shop);

        var product = new Product { Id = Guid.NewGuid(), ShopId = shop.Id, Name = "Shirt", Price = 50, Stock = 5 };
        context.Products.Add(product);

        var order1 = new Order { Id = Guid.NewGuid(), BuyerId = buyer.Id, ShopId = shop.Id, Status = OrderStatus.Pending };
        order1.Items.Add(new OrderItem { OrderId = order1.Id, ProductId = product.Id, Quantity = 1, UnitPrice = 50 });
        context.Orders.Add(order1);
        await context.SaveChangesAsync();

        var sender = new TestRealtimeNotificationSender();
        var notifService = new NotificationService(context, sender, NullLogger<NotificationService>.Instance);
        var orderService = new OrderService(context, notificationService: notifService);

        // Buyer cancels -> ShopOwner receives OrderCancelled
        await orderService.CancelAsync(order1.Id, buyer.Id);
        var shopNotifs = await notifService.GetByUserAsync(shopOwner.Id, new PaginationParams());
        Assert.Contains(shopNotifs.Items, n => n.Type == NotificationType.OrderCancelled.ToString());

        // Create second order: ShopOwner cancels -> Buyer receives OrderCancelled
        var order2 = new Order { Id = Guid.NewGuid(), BuyerId = buyer.Id, ShopId = shop.Id, Status = OrderStatus.Pending };
        order2.Items.Add(new OrderItem { OrderId = order2.Id, ProductId = product.Id, Quantity = 1, UnitPrice = 50 });
        context.Orders.Add(order2);
        await context.SaveChangesAsync();

        await orderService.CancelAsync(order2.Id, shopOwner.Id);
        var buyerNotifs = await notifService.GetByUserAsync(buyer.Id, new PaginationParams());
        Assert.Contains(buyerNotifs.Items, n => n.Type == NotificationType.OrderCancelled.ToString());
    }

    [Fact]
    public async Task Order_InvalidTransition_ThrowsAndDoesNotNotify()
    {
        using var context = TestDbContextFactory.Create(nameof(Order_InvalidTransition_ThrowsAndDoesNotNotify));
        var buyer = new User { Id = Guid.NewGuid(), Username = "buyer", DisplayName = "Buyer" };
        var shopOwner = new User { Id = Guid.NewGuid(), Username = "shopowner", DisplayName = "Shop Owner" };
        var shipper = new User { Id = Guid.NewGuid(), Username = "shipper", DisplayName = "Shipper" };
        context.Users.AddRange(buyer, shopOwner, shipper);

        var shop = new Shop { Id = Guid.NewGuid(), OwnerId = shopOwner.Id, Name = "Shop" };
        context.Shops.Add(shop);

        var order = new Order { Id = Guid.NewGuid(), BuyerId = buyer.Id, ShopId = shop.Id, Status = OrderStatus.Pending };
        context.Orders.Add(order);
        await context.SaveChangesAsync();

        var sender = new TestRealtimeNotificationSender();
        var notifService = new NotificationService(context, sender, NullLogger<NotificationService>.Instance);
        var orderService = new OrderService(context, notificationService: notifService);

        // Pending -> Delivered is illegal
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => orderService.DeliverAsync(order.Id, shipper.Id));
        Assert.Equal(0, await notifService.GetUnreadCountAsync(buyer.Id));
    }

    // ==========================================
    // 4. REALTIME FAILURE RESILIENCE
    // ==========================================
    [Fact]
    public async Task RealtimeFailure_DoesNotRollbackBusinessOperation()
    {
        using var context = TestDbContextFactory.Create(nameof(RealtimeFailure_DoesNotRollbackBusinessOperation));
        var user = new User { Id = Guid.NewGuid(), Username = "user", DisplayName = "User" };
        context.Users.Add(user);
        await context.SaveChangesAsync();

        var sender = new TestRealtimeNotificationSender { ShouldThrow = true }; // Simulate SignalR error
        var notifService = new NotificationService(context, sender, NullLogger<NotificationService>.Instance);

        // Creation must succeed in SQL even though realtime delivery throws
        var notif = await notifService.CreateAsync(user.Id, NotificationType.OrderConfirmed, "Saved in SQL", "Content");
        Assert.NotNull(notif);

        // Notification exists durably in database
        var inDb = await context.Notifications.FindAsync(notif.Id);
        Assert.NotNull(inDb);
    }

    // ==========================================
    // 5. REST & SIGNALR END-TO-END INTEGRATION
    // ==========================================
    private async Task<(string Token, Guid UserId)> AuthenticateUserAsync(string username, string password)
    {
        var loginResponse = await _client.PostAsJsonAsync("/api/auth/login", new LoginRequest
        {
            Username = username,
            Password = password
        });
        loginResponse.EnsureSuccessStatusCode();
        var body = await loginResponse.Content.ReadFromJsonAsync<ApiResponse<AuthResponse>>();
        return (body!.Data!.AccessToken, body.Data.User.Id);
    }

    [Fact]
    public async Task RestApi_NotificationsController_GetAll_UnreadCount_AndMarkRead()
    {
        var (token, userId) = await AuthenticateUserAsync("user1", "User@123");
        using var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        // 1. Get notifications list
        var listRes = await client.GetFromJsonAsync<ApiResponse<PagedResult<NotificationDto>>>("/api/notifications?pageSize=10");
        Assert.NotNull(listRes?.Data);

        // 2. Get unread count
        var countRes = await client.GetFromJsonAsync<ApiResponse<NotificationCountDto>>("/api/notifications/unread-count");
        Assert.NotNull(countRes?.Data);
        Assert.True(countRes.Data.UnreadCount >= 0);

        // 3. Mark all as read
        var readAllRes = await client.PostAsync("/api/notifications/read-all", null);
        Assert.Equal(HttpStatusCode.OK, readAllRes.StatusCode);

        // 4. Verify unread count is 0
        var countAfterRes = await client.GetFromJsonAsync<ApiResponse<NotificationCountDto>>("/api/notifications/unread-count");
        Assert.Equal(0, countAfterRes!.Data!.UnreadCount);
    }

    // ==========================================
    // 6. TWO-USER REALTIME NOTIFICATION & MULTI-DEVICE SIMULATION
    // ==========================================
    [Fact]
    public async Task SignalR_TwoUserRealtimeNotificationFlow_AndMultiDeviceSync()
    {
        var (tokenA, userAId) = await AuthenticateUserAsync("user1", "User@123");
        var (tokenB, userBId) = await AuthenticateUserAsync("shopowner1", "Shop@123");

        // User B connects with Device 1 (Web) and Device 2 (Android simulation)
        var connBWeb = new HubConnectionBuilder()
            .WithUrl(new Uri(_factory.Server.BaseAddress, $"/hubs/chat?access_token={tokenB}"), options =>
            {
                options.HttpMessageHandlerFactory = _ => _factory.Server.CreateHandler();
                options.Transports = HttpTransportType.LongPolling;
            })
            .Build();

        var connBAndroid = new HubConnectionBuilder()
            .WithUrl(new Uri(_factory.Server.BaseAddress, $"/hubs/chat?access_token={tokenB}"), options =>
            {
                options.HttpMessageHandlerFactory = _ => _factory.Server.CreateHandler();
                options.Transports = HttpTransportType.LongPolling;
            })
            .Build();

        await connBWeb.StartAsync();
        await connBAndroid.StartAsync();

        // Listen for realtime notifications on User B's connections
        var notifReceivedTcs = new TaskCompletionSource<NotificationDto>();
        connBWeb.On<NotificationDto>("NotificationReceived", notif => notifReceivedTcs.TrySetResult(notif));

        var webReadTcs = new TaskCompletionSource<NotificationReadEventDto>();
        connBWeb.On<NotificationReadEventDto>("NotificationRead", ev => webReadTcs.TrySetResult(ev));

        var androidReadTcs = new TaskCompletionSource<NotificationReadEventDto>();
        connBAndroid.On<NotificationReadEventDto>("NotificationRead", ev => androidReadTcs.TrySetResult(ev));

        // User A performs action via REST that triggers a notification for User B (Follow)
        using var clientA = _factory.CreateClient();
        clientA.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", tokenA);

        // User A follows User B (or unfollows first if already followed in seed/test)
        await clientA.DeleteAsync($"/api/users/{userBId}/follow");
        var followRes = await clientA.PostAsync($"/api/users/{userBId}/follow", null);
        Assert.Equal(HttpStatusCode.OK, followRes.StatusCode);

        // User B receives NotificationReceived in real time via SignalR
        var waitNotif = await Task.WhenAny(notifReceivedTcs.Task, Task.Delay(5000));
        Assert.Equal(notifReceivedTcs.Task, waitNotif);
        var receivedNotif = await notifReceivedTcs.Task;
        Assert.Equal(NotificationType.NewFollower.ToString(), receivedNotif.Type);

        // Multi-device sync test:
        // User B marks notification as read via REST API (simulating Android app marking read)
        using var clientB = _factory.CreateClient();
        clientB.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", tokenB);

        var readRes = await clientB.PostAsync($"/api/notifications/{receivedNotif.Id}/read", null);
        Assert.Equal(HttpStatusCode.OK, readRes.StatusCode);

        // BOTH Device 1 (Web) and Device 2 (Android) must receive NotificationRead event!
        var waitWebRead = await Task.WhenAny(webReadTcs.Task, Task.Delay(5000));
        Assert.Equal(webReadTcs.Task, waitWebRead);

        var waitAndroidRead = await Task.WhenAny(androidReadTcs.Task, Task.Delay(5000));
        Assert.Equal(androidReadTcs.Task, waitAndroidRead);

        var webEv = await webReadTcs.Task;
        var androidEv = await androidReadTcs.Task;

        Assert.Equal(receivedNotif.Id, webEv.NotificationId);
        Assert.Equal(receivedNotif.Id, androidEv.NotificationId);
        Assert.True(webEv.IsRead);
        Assert.True(androidEv.IsRead);

        await connBWeb.StopAsync();
        await connBAndroid.StopAsync();
    }
}
