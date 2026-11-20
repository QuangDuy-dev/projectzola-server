using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Http.Connections;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.SignalR.Client;
using Microsoft.Extensions.Logging.Abstractions;
using MySocialApp.Application.DTOs.Auth;
using MySocialApp.Application.DTOs.Chat;
using MySocialApp.Application.DTOs.Common;
using MySocialApp.Application.DTOs.Users;
using MySocialApp.Application.Services;
using MySocialApp.Domain.Entities;
using MySocialApp.Domain.Enums;
using MySocialApp.Infrastructure.Services;

namespace MySocialApp.Tests;

public class SignalRChatTests : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly WebApplicationFactory<Program> _factory;
    private readonly HttpClient _client;

    public SignalRChatTests(WebApplicationFactory<Program> factory)
    {
        _factory = factory;
        _client = factory.CreateClient();
    }

    // ==========================================
    // 1. IN-MEMORY USER PRESENCE & MULTI-DEVICE TESTS
    // ==========================================
    [Fact]
    public async Task PresenceService_MultiDeviceTracking_CorrectTransitions()
    {
        var presenceService = new InMemoryUserPresenceService(NullLogger<InMemoryUserPresenceService>.Instance);
        var userId = Guid.NewGuid();

        // Device 1 connects
        var isNewlyOnline1 = await presenceService.UserConnectedAsync(userId, "conn-device-1");
        Assert.True(isNewlyOnline1);
        Assert.True(await presenceService.IsUserOnlineAsync(userId));
        Assert.Equal(1, await presenceService.GetConnectionCountAsync(userId));

        // Device 2 connects
        var isNewlyOnline2 = await presenceService.UserConnectedAsync(userId, "conn-device-2");
        Assert.False(isNewlyOnline2); // Already online
        Assert.True(await presenceService.IsUserOnlineAsync(userId));
        Assert.Equal(2, await presenceService.GetConnectionCountAsync(userId));

        // Device 1 disconnects - user should STILL be online
        var isNewlyOffline1 = await presenceService.UserDisconnectedAsync(userId, "conn-device-1");
        Assert.False(isNewlyOffline1); // Still has conn-device-2
        Assert.True(await presenceService.IsUserOnlineAsync(userId));
        Assert.Equal(1, await presenceService.GetConnectionCountAsync(userId));

        // Device 2 disconnects - user transitions to OFFLINE
        var isNewlyOffline2 = await presenceService.UserDisconnectedAsync(userId, "conn-device-2");
        Assert.True(isNewlyOffline2); // Last connection removed
        Assert.False(await presenceService.IsUserOnlineAsync(userId));
        Assert.Equal(0, await presenceService.GetConnectionCountAsync(userId));
    }

    [Fact]
    public async Task PresenceService_GetOnlineUsers_FiltersAccurately()
    {
        var presenceService = new InMemoryUserPresenceService(NullLogger<InMemoryUserPresenceService>.Instance);
        var u1 = Guid.NewGuid();
        var u2 = Guid.NewGuid();
        var u3 = Guid.NewGuid();

        await presenceService.UserConnectedAsync(u1, "conn-1");
        await presenceService.UserConnectedAsync(u3, "conn-3");

        var online = await presenceService.GetOnlineUsersAsync(new[] { u1, u2, u3 });
        Assert.Contains(u1, online);
        Assert.DoesNotContain(u2, online);
        Assert.Contains(u3, online);
    }

    // ==========================================
    // 2. CHAT SERVICE: IDEMPOTENCY VIA ClientMessageId
    // ==========================================
    [Fact]
    public async Task ChatService_SendMessage_IdempotencyWorks()
    {
        using var context = TestDbContextFactory.Create(nameof(ChatService_SendMessage_IdempotencyWorks));
        var u1 = new User { Id = Guid.NewGuid(), Username = "userA", DisplayName = "User A" };
        var u2 = new User { Id = Guid.NewGuid(), Username = "userB", DisplayName = "User B" };
        context.Users.AddRange(u1, u2);

        var conv = new Conversation { Id = Guid.NewGuid() };
        conv.Members.Add(new ConversationMember { ConversationId = conv.Id, UserId = u1.Id });
        conv.Members.Add(new ConversationMember { ConversationId = conv.Id, UserId = u2.Id });
        context.Conversations.Add(conv);
        await context.SaveChangesAsync();

        var chatService = new ChatService(context);

        var clientMsgId = "client-uuid-" + Guid.NewGuid();
        var request = new SendMessageRequest
        {
            Content = "Hello from Client",
            ClientMessageId = clientMsgId
        };

        // First send
        var firstResult = await chatService.SendMessageAsync(conv.Id, u1.Id, request);
        Assert.NotNull(firstResult);
        Assert.Equal("Hello from Client", firstResult.Content);
        Assert.Equal(clientMsgId, firstResult.ClientMessageId);

        // Retry with SAME ClientMessageId
        var secondResult = await chatService.SendMessageAsync(conv.Id, u1.Id, request);
        Assert.NotNull(secondResult);
        Assert.Equal(firstResult.Id, secondResult.Id); // Exactly same message!

        // Total messages in DB should remain 1
        Assert.Single(context.Messages.Where(m => m.ConversationId == conv.Id));
    }

    // ==========================================
    // 3. CHAT SERVICE: EDIT & SOFT DELETE
    // ==========================================
    [Fact]
    public async Task ChatService_EditMessage_OnlyAuthorCanEdit()
    {
        using var context = TestDbContextFactory.Create(nameof(ChatService_EditMessage_OnlyAuthorCanEdit));
        var u1 = new User { Id = Guid.NewGuid(), Username = "userA", DisplayName = "User A" };
        var u2 = new User { Id = Guid.NewGuid(), Username = "userB", DisplayName = "User B" };
        context.Users.AddRange(u1, u2);

        var conv = new Conversation { Id = Guid.NewGuid() };
        conv.Members.Add(new ConversationMember { ConversationId = conv.Id, UserId = u1.Id });
        conv.Members.Add(new ConversationMember { ConversationId = conv.Id, UserId = u2.Id });
        context.Conversations.Add(conv);

        var msg = new Message { Id = Guid.NewGuid(), ConversationId = conv.Id, SenderId = u1.Id, Content = "Original text" };
        context.Messages.Add(msg);
        await context.SaveChangesAsync();

        var chatService = new ChatService(context);

        // u2 attempts to edit u1's message -> Unauthorized
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() =>
            chatService.EditMessageAsync(msg.Id, u2.Id, new EditMessageRequest { Content = "Hacked" }));

        // u1 edits own message -> Success
        var updated = await chatService.EditMessageAsync(msg.Id, u1.Id, new EditMessageRequest { Content = "Updated text" });
        Assert.Equal("Updated text", updated.Content);
        Assert.NotNull(updated.EditedAt);
    }

    [Fact]
    public async Task ChatService_DeleteMessage_SoftDeleteReturnsPlaceholder()
    {
        using var context = TestDbContextFactory.Create(nameof(ChatService_DeleteMessage_SoftDeleteReturnsPlaceholder));
        var u1 = new User { Id = Guid.NewGuid(), Username = "userA", DisplayName = "User A" };
        var u2 = new User { Id = Guid.NewGuid(), Username = "userB", DisplayName = "User B" };
        context.Users.AddRange(u1, u2);

        var conv = new Conversation { Id = Guid.NewGuid() };
        conv.Members.Add(new ConversationMember { ConversationId = conv.Id, UserId = u1.Id });
        conv.Members.Add(new ConversationMember { ConversationId = conv.Id, UserId = u2.Id });
        context.Conversations.Add(conv);

        var msg = new Message { Id = Guid.NewGuid(), ConversationId = conv.Id, SenderId = u1.Id, Content = "Sensitive message" };
        context.Messages.Add(msg);
        await context.SaveChangesAsync();

        var chatService = new ChatService(context);

        // Delete message
        var deletedEvent = await chatService.DeleteMessageAsync(msg.Id, u1.Id);
        Assert.Equal(msg.Id, deletedEvent.MessageId);
        Assert.Equal(conv.Id, deletedEvent.ConversationId);

        // Fetch messages through GetMessagesAsync
        var history = await chatService.GetMessagesAsync(conv.Id, u2.Id, new PaginationParams());
        Assert.Single(history.Items);
        var fetchedMsg = history.Items.First();
        Assert.True(fetchedMsg.IsDeleted);
        Assert.Equal("[Tin nhắn đã bị thu hồi]", fetchedMsg.Content);

        // Attempting to edit a deleted message should fail
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            chatService.EditMessageAsync(msg.Id, u1.Id, new EditMessageRequest { Content = "New text" }));
    }

    // ==========================================
    // 4. CHAT SERVICE: READ RECEIPTS & UNREAD COUNT
    // ==========================================
    [Fact]
    public async Task ChatService_ReadReceipts_AccuratelyCalculatesUnreadCount()
    {
        using var context = TestDbContextFactory.Create(nameof(ChatService_ReadReceipts_AccuratelyCalculatesUnreadCount));
        var u1 = new User { Id = Guid.NewGuid(), Username = "sender", DisplayName = "Sender" };
        var u2 = new User { Id = Guid.NewGuid(), Username = "receiver", DisplayName = "Receiver" };
        context.Users.AddRange(u1, u2);

        var conv = new Conversation { Id = Guid.NewGuid() };
        conv.Members.Add(new ConversationMember { ConversationId = conv.Id, UserId = u1.Id });
        conv.Members.Add(new ConversationMember { ConversationId = conv.Id, UserId = u2.Id });
        context.Conversations.Add(conv);

        // u1 sends 2 messages to u2
        var m1 = new Message { Id = Guid.NewGuid(), ConversationId = conv.Id, SenderId = u1.Id, Content = "Msg 1", CreatedAt = DateTime.UtcNow.AddMinutes(-5) };
        var m2 = new Message { Id = Guid.NewGuid(), ConversationId = conv.Id, SenderId = u1.Id, Content = "Msg 2", CreatedAt = DateTime.UtcNow.AddMinutes(-2) };
        context.Messages.AddRange(m1, m2);
        await context.SaveChangesAsync();

        var chatService = new ChatService(context);

        // Before reading: receiver has 2 unread messages, sender has 0
        var convsReceiverBefore = await chatService.GetConversationsAsync(u2.Id, new PaginationParams());
        Assert.Equal(2, convsReceiverBefore.Items.First().UnreadCount);

        var convsSender = await chatService.GetConversationsAsync(u1.Id, new PaginationParams());
        Assert.Equal(0, convsSender.Items.First().UnreadCount);

        // Receiver marks m1 as read
        await chatService.MarkAsReadAsync(conv.Id, u2.Id, new MarkReadRequest { MessageId = m1.Id });

        // Unread count should drop to 1 (only m2 unread)
        var convsReceiverAfterM1 = await chatService.GetConversationsAsync(u2.Id, new PaginationParams());
        Assert.Equal(1, convsReceiverAfterM1.Items.First().UnreadCount);

        // Receiver marks m2 as read
        await chatService.MarkAsReadAsync(conv.Id, u2.Id, new MarkReadRequest { MessageId = m2.Id });

        // Unread count should drop to 0
        var convsReceiverAfterM2 = await chatService.GetConversationsAsync(u2.Id, new PaginationParams());
        Assert.Equal(0, convsReceiverAfterM2.Items.First().UnreadCount);
    }

    // ==========================================
    // 5. REST & REALTIME INTEGRATION ENDPOINTS
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
    public async Task RestApi_ChatEndpoints_EditDeleteAndMarkRead_WorkCorrectly()
    {
        var (token, _) = await AuthenticateUserAsync("user1", "User@123");
        var (_, peerId) = await AuthenticateUserAsync("shopowner1", "Shop@123");

        using var authClient = _factory.CreateClient();
        authClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        // Get or create conversation with peer
        var convRes = await authClient.PostAsJsonAsync("/api/conversations", new CreateConversationRequest { ParticipantId = peerId });
        convRes.EnsureSuccessStatusCode();
        var conv = (await convRes.Content.ReadFromJsonAsync<ApiResponse<ConversationDto>>())!.Data!;

        // Send a message via REST
        var sendRes = await authClient.PostAsJsonAsync($"/api/conversations/{conv.Id}/messages", new SendMessageRequest
        {
            Content = "Initial message for REST test",
            ClientMessageId = "rest-test-" + Guid.NewGuid()
        });
        Assert.Equal(HttpStatusCode.Created, sendRes.StatusCode);
        var sentMsg = (await sendRes.Content.ReadFromJsonAsync<ApiResponse<MessageDto>>())!.Data!;

        // Edit the message via REST
        var editRes = await authClient.PutAsJsonAsync($"/api/conversations/{conv.Id}/messages/{sentMsg.Id}", new EditMessageRequest
        {
            Content = "Edited via REST API"
        });
        Assert.Equal(HttpStatusCode.OK, editRes.StatusCode);
        var editedMsg = (await editRes.Content.ReadFromJsonAsync<ApiResponse<MessageDto>>())!.Data!;
        Assert.Equal("Edited via REST API", editedMsg.Content);

        // Mark as read via REST
        var readRes = await authClient.PostAsJsonAsync($"/api/conversations/{conv.Id}/read", new MarkReadRequest
        {
            MessageId = sentMsg.Id
        });
        Assert.Equal(HttpStatusCode.OK, readRes.StatusCode);

        // Delete message via REST
        var deleteRes = await authClient.DeleteAsync($"/api/conversations/{conv.Id}/messages/{sentMsg.Id}");
        Assert.Equal(HttpStatusCode.OK, deleteRes.StatusCode);

        // Verify soft delete in message history
        var historyRes = await authClient.GetFromJsonAsync<ApiResponse<PagedResult<MessageDto>>>($"/api/conversations/{conv.Id}/messages");
        var deletedHistoryMsg = historyRes!.Data!.Items.First(m => m.Id == sentMsg.Id);
        Assert.True(deletedHistoryMsg.IsDeleted);
        Assert.Equal("[Tin nhắn đã bị thu hồi]", deletedHistoryMsg.Content);
    }

    // ==========================================
    // 6. SIGNALR HUB AUTHENTICATION TESTS
    // ==========================================
    [Fact]
    public async Task ChatHub_ConnectWithoutToken_FailsOrRejects()
    {
        var hubConnection = new HubConnectionBuilder()
            .WithUrl(new Uri(_factory.Server.BaseAddress, "/hubs/chat"), options =>
            {
                options.HttpMessageHandlerFactory = _ => _factory.Server.CreateHandler();
                options.Transports = HttpTransportType.LongPolling;
            })
            .Build();

        // Without JWT token, connecting should throw HttpRequestException with 401 Unauthorized
        await Assert.ThrowsAnyAsync<Exception>(() => hubConnection.StartAsync());
    }

    [Fact]
    public async Task ChatHub_ConnectWithValidTokenQueryString_ConnectsSuccessfully()
    {
        var (token, _) = await AuthenticateUserAsync("user1", "User@123");

        var hubConnection = new HubConnectionBuilder()
            .WithUrl(new Uri(_factory.Server.BaseAddress, $"/hubs/chat?access_token={token}"), options =>
            {
                options.HttpMessageHandlerFactory = _ => _factory.Server.CreateHandler();
                options.Transports = HttpTransportType.LongPolling;
            })
            .Build();

        await hubConnection.StartAsync();
        Assert.Equal(HubConnectionState.Connected, hubConnection.State);

        await hubConnection.StopAsync();
        Assert.Equal(HubConnectionState.Disconnected, hubConnection.State);
    }

    // ==========================================
    // 7. TWO-USER REALTIME SIGNALR COMMUNICATION TEST
    // ==========================================
    [Fact]
    public async Task ChatHub_TwoUserRealtimeFlow_MessagesAndTypingDelivered()
    {
        var (token1, _) = await AuthenticateUserAsync("user1", "User@123");
        var (token2, user2Id) = await AuthenticateUserAsync("shopowner1", "Shop@123");

        // User1 and User2 connect via SignalR
        var conn1 = new HubConnectionBuilder()
            .WithUrl(new Uri(_factory.Server.BaseAddress, $"/hubs/chat?access_token={token1}"), options =>
            {
                options.HttpMessageHandlerFactory = _ => _factory.Server.CreateHandler();
                options.Transports = HttpTransportType.LongPolling;
            })
            .Build();

        var conn2 = new HubConnectionBuilder()
            .WithUrl(new Uri(_factory.Server.BaseAddress, $"/hubs/chat?access_token={token2}"), options =>
            {
                options.HttpMessageHandlerFactory = _ => _factory.Server.CreateHandler();
                options.Transports = HttpTransportType.LongPolling;
            })
            .Build();

        await conn1.StartAsync();
        await conn2.StartAsync();

        // Prepare conversation via REST
        using var client1 = _factory.CreateClient();
        client1.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token1);

        var convRes = await client1.PostAsJsonAsync("/api/conversations", new CreateConversationRequest { ParticipantId = user2Id });
        var conv = (await convRes.Content.ReadFromJsonAsync<ApiResponse<ConversationDto>>())!.Data!;

        // Join conversation room in SignalR
        await conn1.InvokeAsync("JoinConversation", conv.Id);
        await conn2.InvokeAsync("JoinConversation", conv.Id);

        // Setup User2 listener for ReceiveMessage & UserTyping
        var receivedMessageTcs = new TaskCompletionSource<MessageDto>();
        conn2.On<MessageDto>("ReceiveMessage", msg => receivedMessageTcs.TrySetResult(msg));

        var typingTcs = new TaskCompletionSource<TypingEventDto>();
        conn2.On<TypingEventDto>("UserTyping", ev => typingTcs.TrySetResult(ev));

        // User1 triggers TypingStarted
        await conn1.InvokeAsync("TypingStarted", conv.Id);
        var typingEvent = await Task.WhenAny(typingTcs.Task, Task.Delay(5000));
        Assert.Equal(typingTcs.Task, typingEvent);
        var typingResult = await typingTcs.Task;
        Assert.True(typingResult.IsTyping);
        Assert.Equal(conv.Id, typingResult.ConversationId);

        // User1 sends message via SignalR hub
        var sentMsg = await conn1.InvokeAsync<MessageDto>("SendMessage", conv.Id, new SendMessageRequest
        {
            Content = "Hello from User 1 in Realtime!",
            ClientMessageId = "rt-test-" + Guid.NewGuid()
        });

        Assert.Equal("Hello from User 1 in Realtime!", sentMsg.Content);

        // User2 receives message in real time
        var completedTask = await Task.WhenAny(receivedMessageTcs.Task, Task.Delay(5000));
        Assert.Equal(receivedMessageTcs.Task, completedTask);
        var receivedMsg = await receivedMessageTcs.Task;
        Assert.Equal("Hello from User 1 in Realtime!", receivedMsg.Content);
        Assert.Equal(sentMsg.Id, receivedMsg.Id);

        await conn1.StopAsync();
        await conn2.StopAsync();
    }
}
