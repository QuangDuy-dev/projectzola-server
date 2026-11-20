using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Http.Connections;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.SignalR.Client;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using MySocialApp.Api.Controllers;
using MySocialApp.Application.DTOs.Auth;
using MySocialApp.Application.DTOs.Common;
using MySocialApp.Application.DTOs.Presence;
using MySocialApp.Application.Services;
using MySocialApp.Domain.Entities;
using MySocialApp.Domain.Enums;
using MySocialApp.Infrastructure.Data;
using MySocialApp.Infrastructure.Services;

namespace MySocialApp.Tests;

public class PresenceTests : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly WebApplicationFactory<Program> _factory;

    public PresenceTests(WebApplicationFactory<Program> factory)
    {
        _factory = factory;
    }

    private static User MakeUser(string name, bool isActive = true, DateTime? lastSeenAt = null) => new()
    {
        Id = Guid.NewGuid(),
        Username = $"p_{name}_{Guid.NewGuid():N}"[..16],
        Email = $"{name}_{Guid.NewGuid():N}@presence.local",
        DisplayName = name,
        IsActive = isActive,
        LastSeenAt = lastSeenAt
    };

    private async Task<(Guid UserId, string Token, HttpClient Client)> CreateAuthenticatedApiUserAsync(string prefix, string displayName)
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
                Email = $"{username}@presence.local",
                DisplayName = displayName,
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
        return (auth.User.Id, auth.AccessToken, client);
    }

    // =========================================================================
    // 1. UNIT TESTS: PresenceQueryService
    // =========================================================================

    [Fact]
    public async Task GetOnlineFriends_ReturnsMutualFollowAndDirectContacts_ExcludesOneWayAndInactive_SortsOnlineFirst()
    {
        using var context = TestDbContextFactory.Create(nameof(GetOnlineFriends_ReturnsMutualFollowAndDirectContacts_ExcludesOneWayAndInactive_SortsOnlineFirst));

        var now = DateTime.UtcNow;
        var me = MakeUser("Me");
        var mutualOnline = MakeUser("Zebra Mutual Online", lastSeenAt: now.AddMinutes(-30));
        var mutualOfflineRecent = MakeUser("Alpha Mutual Offline", lastSeenAt: now.AddMinutes(-5));
        var directOnline = MakeUser("Beta Direct Online", lastSeenAt: now.AddHours(-1));
        var directOfflineOld = MakeUser("Gamma Direct Offline", lastSeenAt: now.AddHours(-3));
        var mutualAndDirect = MakeUser("Delta Both", lastSeenAt: now.AddMinutes(-15));
        var oneWayFollowing = MakeUser("OneWay Outgoing");
        var oneWayFollower = MakeUser("OneWay Incoming");
        var groupOnlyMember = MakeUser("Group Only");
        var inactiveMutual = MakeUser("Inactive Mutual", isActive: false);
        var inactiveDirect = MakeUser("Inactive Direct", isActive: false);

        context.Users.AddRange(
            me, mutualOnline, mutualOfflineRecent, directOnline, directOfflineOld,
            mutualAndDirect, oneWayFollowing, oneWayFollower, groupOnlyMember,
            inactiveMutual, inactiveDirect);

        // Mutual follows: me <-> mutualOnline, me <-> mutualOfflineRecent, me <-> mutualAndDirect, me <-> inactiveMutual
        context.Follows.AddRange(
            new Follow { FollowerId = me.Id, FollowingId = mutualOnline.Id },
            new Follow { FollowerId = mutualOnline.Id, FollowingId = me.Id },
            new Follow { FollowerId = me.Id, FollowingId = mutualOfflineRecent.Id },
            new Follow { FollowerId = mutualOfflineRecent.Id, FollowingId = me.Id },
            new Follow { FollowerId = me.Id, FollowingId = mutualAndDirect.Id },
            new Follow { FollowerId = mutualAndDirect.Id, FollowingId = me.Id },
            new Follow { FollowerId = me.Id, FollowingId = inactiveMutual.Id },
            new Follow { FollowerId = inactiveMutual.Id, FollowingId = me.Id },
            // 1-way follows (must be excluded)
            new Follow { FollowerId = me.Id, FollowingId = oneWayFollowing.Id },
            new Follow { FollowerId = oneWayFollower.Id, FollowingId = me.Id }
        );

        // Direct conversations: me <-> directOnline, me <-> directOfflineOld, me <-> mutualAndDirect, me <-> inactiveDirect
        void AddConv(ConversationType type, params Guid[] userIds)
        {
            var c = new Conversation { Id = Guid.NewGuid(), Type = type };
            foreach (var uid in userIds)
                c.Members.Add(new ConversationMember { ConversationId = c.Id, UserId = uid });
            context.Conversations.Add(c);
        }

        AddConv(ConversationType.Direct, me.Id, directOnline.Id);
        AddConv(ConversationType.Direct, me.Id, directOfflineOld.Id);
        AddConv(ConversationType.Direct, me.Id, mutualAndDirect.Id);
        AddConv(ConversationType.Direct, me.Id, inactiveDirect.Id);
        // Group-only conversation must NOT make groupOnlyMember a DirectContact
        AddConv(ConversationType.Group, me.Id, groupOnlyMember.Id);

        await context.SaveChangesAsync();

        var presenceService = new InMemoryUserPresenceService(NullLogger<InMemoryUserPresenceService>.Instance);
        await presenceService.UserConnectedAsync(mutualOnline.Id, "conn-1");
        await presenceService.UserConnectedAsync(directOnline.Id, "conn-2");
        await presenceService.UserConnectedAsync(oneWayFollowing.Id, "conn-3");
        await presenceService.UserConnectedAsync(inactiveMutual.Id, "conn-4");

        var queryService = new PresenceQueryService(context, presenceService);
        var friends = await queryService.GetOnlineFriendsAsync(me.Id);

        // Expect exactly 5 active eligible contacts (no duplicates, no 1-way, no group-only, no inactive)
        Assert.Equal(5, friends.Count);

        // Excluded users
        Assert.DoesNotContain(friends, f => f.UserId == oneWayFollowing.Id);
        Assert.DoesNotContain(friends, f => f.UserId == oneWayFollower.Id);
        Assert.DoesNotContain(friends, f => f.UserId == groupOnlyMember.Id);
        Assert.DoesNotContain(friends, f => f.UserId == inactiveMutual.Id);
        Assert.DoesNotContain(friends, f => f.UserId == inactiveDirect.Id);

        // Relationships accurately labelled (MutualFollow takes precedence when both apply)
        Assert.Equal("MutualFollow", friends.Single(f => f.UserId == mutualOnline.Id).Relationship);
        Assert.Equal("MutualFollow", friends.Single(f => f.UserId == mutualOfflineRecent.Id).Relationship);
        Assert.Equal("MutualFollow", friends.Single(f => f.UserId == mutualAndDirect.Id).Relationship);
        Assert.Equal("DirectContact", friends.Single(f => f.UserId == directOnline.Id).Relationship);
        Assert.Equal("DirectContact", friends.Single(f => f.UserId == directOfflineOld.Id).Relationship);

        // Sorted: online first, then LastSeenAt DESC
        Assert.True(friends[0].IsOnline);
        Assert.True(friends[1].IsOnline);
        Assert.Equal(mutualOnline.Id, friends[0].UserId); // online + more recent LastSeenAt (-30m vs -1h)
        Assert.Equal(directOnline.Id, friends[1].UserId);

        Assert.False(friends[2].IsOnline);
        Assert.False(friends[3].IsOnline);
        Assert.False(friends[4].IsOnline);
        Assert.Equal(mutualOfflineRecent.Id, friends[2].UserId); // -5m
        Assert.Equal(mutualAndDirect.Id, friends[3].UserId);     // -15m
        Assert.Equal(directOfflineOld.Id, friends[4].UserId);    // -3h
    }

    [Fact]
    public async Task GetBatchPresence_DeduplicatesAndExcludesInactiveUsers()
    {
        using var context = TestDbContextFactory.Create(nameof(GetBatchPresence_DeduplicatesAndExcludesInactiveUsers));
        var lastSeen = DateTime.UtcNow.AddMinutes(-10);
        var uOnline = MakeUser("OnlineU");
        var uOffline = MakeUser("OfflineU", lastSeenAt: lastSeen);
        var uInactive = MakeUser("InactiveU", isActive: false);
        context.Users.AddRange(uOnline, uOffline, uInactive);
        await context.SaveChangesAsync();

        var presenceService = new InMemoryUserPresenceService(NullLogger<InMemoryUserPresenceService>.Instance);
        await presenceService.UserConnectedAsync(uOnline.Id, "conn-a");

        var queryService = new PresenceQueryService(context, presenceService);
        var statuses = await queryService.GetBatchPresenceAsync(new[]
        {
            uOnline.Id, uOnline.Id, Guid.Empty, uOffline.Id, uInactive.Id, Guid.NewGuid()
        });

        Assert.Equal(2, statuses.Count);
        var onlineDto = Assert.Single(statuses, s => s.UserId == uOnline.Id);
        Assert.True(onlineDto.IsOnline);

        var offlineDto = Assert.Single(statuses, s => s.UserId == uOffline.Id);
        Assert.False(offlineDto.IsOnline);
        Assert.Equal(lastSeen, offlineDto.LastSeenAt);
    }

    [Fact]
    public async Task UpdateLastSeenAsync_PersistsUtcTimestamp_AndIgnoresUnknownUser()
    {
        using var context = TestDbContextFactory.Create(nameof(UpdateLastSeenAsync_PersistsUtcTimestamp_AndIgnoresUnknownUser));
        var user = MakeUser("SeenUser", lastSeenAt: null);
        context.Users.Add(user);
        await context.SaveChangesAsync();

        var presenceService = new InMemoryUserPresenceService(NullLogger<InMemoryUserPresenceService>.Instance);
        var queryService = new PresenceQueryService(context, presenceService);

        Assert.Null(user.LastSeenAt);

        var before = DateTime.UtcNow.AddSeconds(-1);
        await queryService.UpdateLastSeenAsync(user.Id);
        var after = DateTime.UtcNow.AddSeconds(1);

        var reloaded = await context.Users.AsNoTracking().FirstAsync(u => u.Id == user.Id);
        Assert.NotNull(reloaded.LastSeenAt);
        Assert.InRange(reloaded.LastSeenAt!.Value, before, after);

        // Missing user does not throw
        var ex = await Record.ExceptionAsync(() => queryService.UpdateLastSeenAsync(Guid.NewGuid()));
        Assert.Null(ex);
    }

    // =========================================================================
    // 2. END-TO-END HTTP + SIGNALR PRESENCE INTEGRATION TESTS
    // =========================================================================

    [Fact]
    public async Task Api_PresenceBatchAndOnlineFriends_AndHubDisconnectUpdatesLastSeen()
    {
        var me = await CreateAuthenticatedApiUserAsync("prme", "Presence Me");
        var mutual = await CreateAuthenticatedApiUserAsync("prmut", "Presence Mutual");
        var direct = await CreateAuthenticatedApiUserAsync("prdir", "Presence Direct");
        var oneWay = await CreateAuthenticatedApiUserAsync("prow", "Presence OneWay");

        using var meClient = me.Client;
        using var mutualClient = mutual.Client;
        using var directClient = direct.Client;
        using var oneWayClient = oneWay.Client;

        // Seed relationships directly in DB (mutual follow, 1-way follow, direct conversation)
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<MySocialAppDbContext>();
            db.Follows.AddRange(
                new Follow { FollowerId = me.UserId, FollowingId = mutual.UserId },
                new Follow { FollowerId = mutual.UserId, FollowingId = me.UserId },
                new Follow { FollowerId = me.UserId, FollowingId = oneWay.UserId }
            );
            var directConv = new Conversation { Type = ConversationType.Direct, IsInviteLinkEnabled = false };
            directConv.Members.Add(new ConversationMember { UserId = me.UserId, Role = ConversationMemberRole.Member });
            directConv.Members.Add(new ConversationMember { UserId = direct.UserId, Role = ConversationMemberRole.Member });
            db.Conversations.Add(directConv);
            await db.SaveChangesAsync();
        }

        // Connect 'mutual' to ChatHub so it becomes ONLINE
        var mutualHub = new HubConnectionBuilder()
            .WithUrl(new Uri(_factory.Server.BaseAddress, $"/hubs/chat?access_token={mutual.Token}"), options =>
            {
                options.HttpMessageHandlerFactory = _ => _factory.Server.CreateHandler();
                options.Transports = HttpTransportType.LongPolling;
            })
            .Build();

        await mutualHub.StartAsync();

        try
        {
            // 1. GET /api/presence/online-friends
            var friendsRes = await meClient.GetAsync("/api/presence/online-friends");
            Assert.Equal(HttpStatusCode.OK, friendsRes.StatusCode);
            var friends = (await friendsRes.Content.ReadFromJsonAsync<ApiResponse<List<OnlineFriendDto>>>())!.Data!;

            Assert.Equal(2, friends.Count);
            Assert.DoesNotContain(friends, f => f.UserId == oneWay.UserId);

            // Online mutual friend sorted first
            Assert.Equal(mutual.UserId, friends[0].UserId);
            Assert.True(friends[0].IsOnline);
            Assert.Equal("MutualFollow", friends[0].Relationship);

            Assert.Equal(direct.UserId, friends[1].UserId);
            Assert.False(friends[1].IsOnline);
            Assert.Equal("DirectContact", friends[1].Relationship);

            // 2. POST /api/presence/batch
            var batchRes = await meClient.PostAsJsonAsync("/api/presence/batch", new BatchPresenceRequest
            {
                UserIds = new List<Guid> { mutual.UserId, direct.UserId }
            });
            Assert.Equal(HttpStatusCode.OK, batchRes.StatusCode);
            var batch = (await batchRes.Content.ReadFromJsonAsync<ApiResponse<List<UserPresenceStatusDto>>>())!.Data!;
            Assert.Equal(2, batch.Count);
            Assert.True(batch.Single(x => x.UserId == mutual.UserId).IsOnline);
            Assert.False(batch.Single(x => x.UserId == direct.UserId).IsOnline);
        }
        finally
        {
            await mutualHub.StopAsync();
            await mutualHub.DisposeAsync();
        }

        // 3. After disconnect, ChatHub.OnDisconnectedAsync persists User.LastSeenAt and marks user offline
        UserPresenceStatusDto? mutualAfter = null;
        for (int attempt = 0; attempt < 25; attempt++)
        {
            var pollRes = await meClient.PostAsJsonAsync("/api/presence/batch", new BatchPresenceRequest
            {
                UserIds = new List<Guid> { mutual.UserId }
            });
            pollRes.EnsureSuccessStatusCode();
            var list = (await pollRes.Content.ReadFromJsonAsync<ApiResponse<List<UserPresenceStatusDto>>>())!.Data!;
            mutualAfter = list.SingleOrDefault(x => x.UserId == mutual.UserId);
            if (mutualAfter is { IsOnline: false, LastSeenAt: not null })
                break;

            await Task.Delay(100);
        }

        Assert.NotNull(mutualAfter);
        Assert.False(mutualAfter!.IsOnline);
        Assert.NotNull(mutualAfter.LastSeenAt);

        // 4. Empty batch returns 200 OK empty list & unauthenticated returns 401
        var emptyBatchRes = await meClient.PostAsJsonAsync("/api/presence/batch", new BatchPresenceRequest { UserIds = new List<Guid>() });
        Assert.Equal(HttpStatusCode.OK, emptyBatchRes.StatusCode);
        var emptyList = (await emptyBatchRes.Content.ReadFromJsonAsync<ApiResponse<List<UserPresenceStatusDto>>>())!.Data!;
        Assert.Empty(emptyList);

        using var anonClient = _factory.CreateClient();
        var unauthRes = await anonClient.GetAsync("/api/presence/online-friends");
        Assert.Equal(HttpStatusCode.Unauthorized, unauthRes.StatusCode);
    }
}
