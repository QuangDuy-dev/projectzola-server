using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using MySocialApp.Api.Controllers;
using MySocialApp.Application.DTOs.Auth;
using MySocialApp.Application.DTOs.Chat;
using MySocialApp.Application.DTOs.Common;
using MySocialApp.Application.Services;
using MySocialApp.Domain.Entities;
using MySocialApp.Domain.Enums;
using MySocialApp.Infrastructure.Data;
using MySocialApp.Infrastructure.Services;

namespace MySocialApp.Tests;

public class ZaloGroupChatTests : IClassFixture<WebApplicationFactory<Program>>, IDisposable
{
    private readonly WebApplicationFactory<Program> _factory;
    private readonly string _tempStorageDir;

    public ZaloGroupChatTests(WebApplicationFactory<Program> baseFactory)
    {
        _tempStorageDir = Path.Combine(Path.GetTempPath(), "MySocialApp_GroupTests_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempStorageDir);

        _factory = baseFactory.WithWebHostBuilder(builder =>
        {
            builder.ConfigureAppConfiguration((_, config) =>
            {
                config.AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["MediaStorage:RootPath"] = _tempStorageDir
                });
            });
        });
    }

    public void Dispose()
    {
        _factory.Dispose();
        try
        {
            if (Directory.Exists(_tempStorageDir))
                Directory.Delete(_tempStorageDir, recursive: true);
        }
        catch
        {
            // Best-effort cleanup
        }
    }

    private static User CreateUser(string suffix, bool isActive = true) => new()
    {
        Id = Guid.NewGuid(),
        Username = $"u_{suffix}_{Guid.NewGuid():N}"[..16],
        Email = $"{suffix}_{Guid.NewGuid():N}@test.local",
        DisplayName = $"User {suffix}",
        IsActive = isActive
    };

    private async Task<(Guid UserId, string Username, string Token, HttpClient Client)> CreateAuthenticatedApiUserAsync(string prefix)
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
                DisplayName = $"Test {prefix}",
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
        var loginBody = (await loginRes.Content.ReadFromJsonAsync<ApiResponse<AuthResponse>>())!.Data!;
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", loginBody.AccessToken);

        return (loginBody.User.Id, username, loginBody.AccessToken, client);
    }

    // =========================================================================
    // 1. GROUP CREATION & DIRECT CHAT NON-COLLISION
    // =========================================================================

    [Fact]
    public async Task CreateGroup_AssignsOwnerGeneratesInviteCodeAndCreatesSystemMessage()
    {
        using var context = TestDbContextFactory.Create(nameof(CreateGroup_AssignsOwnerGeneratesInviteCodeAndCreatesSystemMessage));
        var owner = CreateUser("owner");
        var m1 = CreateUser("m1");
        var m2 = CreateUser("m2");
        context.Users.AddRange(owner, m1, m2);
        await context.SaveChangesAsync();

        var presence = new InMemoryUserPresenceService(NullLogger<InMemoryUserPresenceService>.Instance);
        var groupService = new GroupChatService(context, presence);

        var dto = await groupService.CreateGroupAsync(owner.Id, new CreateGroupConversationRequest
        {
            Name = "Project Zola Core",
            MemberIds = new List<Guid> { m1.Id, m2.Id },
            IsInviteLinkEnabled = true
        });

        Assert.Equal("Group", dto.Type);
        Assert.Equal("Project Zola Core", dto.Name);
        Assert.Equal(owner.Id, dto.CreatedById);
        Assert.False(dto.IsDisbanded);
        Assert.True(dto.IsInviteLinkEnabled);
        Assert.NotNull(dto.InviteCode);
        Assert.Equal(12, dto.InviteCode!.Length);
        Assert.Equal("Owner", dto.CurrentUserRole);

        Assert.Equal(3, dto.Members.Count);
        Assert.Equal("Owner", dto.Members.Single(m => m.UserId == owner.Id).Role);
        Assert.Equal("Member", dto.Members.Single(m => m.UserId == m1.Id).Role);
        Assert.Equal("Member", dto.Members.Single(m => m.UserId == m2.Id).Role);

        // System message created in DB and exposed as LastMessage
        Assert.NotNull(dto.LastMessage);
        Assert.Equal("System", dto.LastMessage!.Type);
        Assert.Contains("Project Zola Core", dto.LastMessage.Content);

        var sysMessages = await context.Messages.Where(m => m.ConversationId == dto.Id).ToListAsync();
        Assert.Single(sysMessages);
        Assert.Equal(MessageType.System, sysMessages[0].Type);
    }

    [Fact]
    public async Task DirectChat_GetOrCreateConversation_NeverCollidesWithTwoMemberGroup()
    {
        using var context = TestDbContextFactory.Create(nameof(DirectChat_GetOrCreateConversation_NeverCollidesWithTwoMemberGroup));
        var alice = CreateUser("alice");
        var bob = CreateUser("bob");
        context.Users.AddRange(alice, bob);
        await context.SaveChangesAsync();

        var groupService = new GroupChatService(context);
        var chatService = new ChatService(context);

        // First create a 2-member Group with Alice and Bob
        var twoPersonGroup = await groupService.CreateGroupAsync(alice.Id, new CreateGroupConversationRequest
        {
            Name = "Duo Group",
            MemberIds = new List<Guid> { bob.Id }
        });
        Assert.Equal("Group", twoPersonGroup.Type);
        Assert.Equal(2, twoPersonGroup.Members.Count);

        // Now request a Direct conversation between Alice and Bob
        var direct = await chatService.GetOrCreateConversationAsync(alice.Id, bob.Id);
        Assert.Equal("Direct", direct.Type);
        Assert.NotEqual(twoPersonGroup.Id, direct.Id);
        Assert.False(direct.IsInviteLinkEnabled);
        Assert.Null(direct.InviteCode);

        // Calling again from Bob's side returns the exact same Direct conversation (never the Group)
        var directAgain = await chatService.GetOrCreateConversationAsync(bob.Id, alice.Id);
        Assert.Equal(direct.Id, directAgain.Id);
        Assert.Equal("Direct", directAgain.Type);
    }

    // =========================================================================
    // 2. THREE-TIER RBAC & OnlyAdminsCanSend
    // =========================================================================

    [Fact]
    public async Task GroupRbac_ThreeTierPermissions_EnforcedStrictly()
    {
        using var context = TestDbContextFactory.Create(nameof(GroupRbac_ThreeTierPermissions_EnforcedStrictly));
        var owner = CreateUser("owner");
        var admin1 = CreateUser("admin1");
        var admin2 = CreateUser("admin2");
        var member = CreateUser("member");
        context.Users.AddRange(owner, admin1, admin2, member);
        await context.SaveChangesAsync();

        var groupService = new GroupChatService(context);
        var group = await groupService.CreateGroupAsync(owner.Id, new CreateGroupConversationRequest
        {
            Name = "RBAC Group",
            MemberIds = new List<Guid> { admin1.Id, admin2.Id, member.Id }
        });

        // Owner promotes admin1 and admin2 to Admin
        var promote1 = await groupService.UpdateMemberRoleAsync(group.Id, owner.Id, admin1.Id, new UpdateMemberRoleRequest { Role = "Admin" });
        Assert.Equal("MemberRoleUpdated", promote1.EventType);
        Assert.Equal("Admin", promote1.Conversation!.Members.Single(m => m.UserId == admin1.Id).Role);
        Assert.NotNull(promote1.SystemMessage);

        await groupService.UpdateMemberRoleAsync(group.Id, owner.Id, admin2.Id, new UpdateMemberRoleRequest { Role = "Admin" });

        // Owner cannot assign Owner via UpdateMemberRoleAsync
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            groupService.UpdateMemberRoleAsync(group.Id, owner.Id, member.Id, new UpdateMemberRoleRequest { Role = "Owner" }));

        // Admin and Member CANNOT promote or demote
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() =>
            groupService.UpdateMemberRoleAsync(group.Id, admin1.Id, member.Id, new UpdateMemberRoleRequest { Role = "Admin" }));
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() =>
            groupService.UpdateMemberRoleAsync(group.Id, member.Id, admin1.Id, new UpdateMemberRoleRequest { Role = "Member" }));

        // Owner demotes admin2 back to Member, then re-promotes to Admin
        var demote = await groupService.UpdateMemberRoleAsync(group.Id, owner.Id, admin2.Id, new UpdateMemberRoleRequest { Role = "Member" });
        Assert.Equal("Member", demote.Conversation!.Members.Single(m => m.UserId == admin2.Id).Role);
        await groupService.UpdateMemberRoleAsync(group.Id, owner.Id, admin2.Id, new UpdateMemberRoleRequest { Role = "Admin" });

        // Admin CANNOT kick Owner or another Admin
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() =>
            groupService.RemoveMemberAsync(group.Id, admin1.Id, owner.Id));
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() =>
            groupService.RemoveMemberAsync(group.Id, admin1.Id, admin2.Id));

        // Member CANNOT kick anyone or update group settings
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() =>
            groupService.RemoveMemberAsync(group.Id, member.Id, admin1.Id));
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() =>
            groupService.UpdateGroupSettingsAsync(group.Id, member.Id, new UpdateGroupSettingsRequest { Name = "Hacked" }));

        // Admin CAN update group settings and kick a regular Member
        var settingsUpdate = await groupService.UpdateGroupSettingsAsync(group.Id, admin1.Id, new UpdateGroupSettingsRequest { Name = "RBAC Group v2" });
        Assert.Equal("RBAC Group v2", settingsUpdate.Conversation!.Name);

        var kickMember = await groupService.RemoveMemberAsync(group.Id, admin1.Id, member.Id);
        Assert.Equal("MemberRemoved", kickMember.EventType);
        Assert.DoesNotContain(kickMember.Conversation!.Members, m => m.UserId == member.Id);

        // Owner CAN kick an Admin
        var kickAdmin = await groupService.RemoveMemberAsync(group.Id, owner.Id, admin2.Id);
        Assert.DoesNotContain(kickAdmin.Conversation!.Members, m => m.UserId == admin2.Id);
    }

    [Fact]
    public async Task OnlyAdminsCanSend_BlocksMemberMessagesAndPolls_AllowsOwnerAndAdmin()
    {
        using var context = TestDbContextFactory.Create(nameof(OnlyAdminsCanSend_BlocksMemberMessagesAndPolls_AllowsOwnerAndAdmin));
        var owner = CreateUser("owner");
        var admin = CreateUser("admin");
        var member = CreateUser("member");
        context.Users.AddRange(owner, admin, member);
        await context.SaveChangesAsync();

        var groupService = new GroupChatService(context);
        var chatService = new ChatService(context);
        var interactionService = new ChatInteractionService(context);

        var group = await groupService.CreateGroupAsync(owner.Id, new CreateGroupConversationRequest
        {
            Name = "Announcements",
            MemberIds = new List<Guid> { admin.Id, member.Id },
            OnlyAdminsCanSend = true
        });
        await groupService.UpdateMemberRoleAsync(group.Id, owner.Id, admin.Id, new UpdateMemberRoleRequest { Role = "Admin" });

        // Member blocked from sending messages and creating polls
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() =>
            chatService.SendMessageAsync(group.Id, member.Id, new SendMessageRequest { Content = "Can I talk?" }));
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() =>
            interactionService.CreatePollAsync(group.Id, member.Id, new CreatePollRequest
            {
                Question = "Blocked poll?",
                Options = new List<string> { "Yes", "No" }
            }));

        // Owner & Admin allowed
        var ownerMsg = await chatService.SendMessageAsync(group.Id, owner.Id, new SendMessageRequest { Content = "Owner announcement" });
        var adminMsg = await chatService.SendMessageAsync(group.Id, admin.Id, new SendMessageRequest { Content = "Admin announcement" });
        Assert.Equal("Owner announcement", ownerMsg.Content);
        Assert.Equal("Admin announcement", adminMsg.Content);

        // Disable restriction -> Member can now send
        await groupService.UpdateGroupSettingsAsync(group.Id, owner.Id, new UpdateGroupSettingsRequest { OnlyAdminsCanSend = false });
        var memberMsg = await chatService.SendMessageAsync(group.Id, member.Id, new SendMessageRequest { Content = "Now I can talk!" });
        Assert.Equal("Now I can talk!", memberMsg.Content);
    }

    // =========================================================================
    // 3. OWNERSHIP TRANSFER, OWNER LEAVE GUARD & DISBAND
    // =========================================================================

    [Fact]
    public async Task OwnershipTransfer_OwnerLeaveGuard_AndLastMemberAutoDisband()
    {
        using var context = TestDbContextFactory.Create(nameof(OwnershipTransfer_OwnerLeaveGuard_AndLastMemberAutoDisband));
        var owner = CreateUser("owner");
        var member1 = CreateUser("m1");
        var member2 = CreateUser("m2");
        var pendingOutsider = CreateUser("outsider");
        context.Users.AddRange(owner, member1, member2, pendingOutsider);
        await context.SaveChangesAsync();

        var groupService = new GroupChatService(context);
        var group = await groupService.CreateGroupAsync(owner.Id, new CreateGroupConversationRequest
        {
            Name = "Succession Group",
            MemberIds = new List<Guid> { member1.Id, member2.Id },
            RequireJoinApproval = true
        });

        // Non-owner cannot transfer ownership
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() =>
            groupService.TransferOwnershipAsync(group.Id, member1.Id, new TransferOwnershipRequest { NewOwnerUserId = member2.Id }));

        // Owner transfers ownership to member1 -> old owner becomes Admin
        var transferred = await groupService.TransferOwnershipAsync(group.Id, owner.Id, new TransferOwnershipRequest { NewOwnerUserId = member1.Id });
        Assert.Equal("OwnershipTransferred", transferred.EventType);
        Assert.Equal("Admin", transferred.Conversation!.Members.Single(m => m.UserId == owner.Id).Role);
        Assert.Equal("Owner", transferred.Conversation.Members.Single(m => m.UserId == member1.Id).Role);

        // New owner (member1) CANNOT leave without specifying NewOwnerUserId while others remain
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            groupService.LeaveGroupAsync(group.Id, member1.Id));

        // New owner leaves WITH NewOwnerUserId = member2 -> succeeds and member2 becomes Owner
        var ownerLeft = await groupService.LeaveGroupAsync(group.Id, member1.Id, new LeaveGroupRequest { NewOwnerUserId = member2.Id });
        Assert.Equal("MemberLeft", ownerLeft.EventType);
        Assert.DoesNotContain(ownerLeft.Conversation!.Members, m => m.UserId == member1.Id);
        Assert.Equal("Owner", ownerLeft.Conversation.Members.Single(m => m.UserId == member2.Id).Role);

        // Old owner (now Admin) leaves normally without NewOwnerUserId
        var adminLeft = await groupService.LeaveGroupAsync(group.Id, owner.Id);
        Assert.Equal("MemberLeft", adminLeft.EventType);
        Assert.Single(adminLeft.Conversation!.Members);

        // Seed a pending join request before last member leaves
        await groupService.JoinByInviteCodeAsync(group.InviteCode!, pendingOutsider.Id);

        // Last remaining member (member2, Owner) leaves -> auto-disbands group and rejects pending requests
        var lastLeft = await groupService.LeaveGroupAsync(group.Id, member2.Id);
        Assert.Equal("GroupDisbanded", lastLeft.EventType);
        Assert.True(lastLeft.Conversation!.IsDisbanded);
        Assert.False(lastLeft.Conversation.IsInviteLinkEnabled);
        Assert.Empty(lastLeft.Conversation.Members);

        var persistedConv = await context.Conversations.FirstAsync(c => c.Id == group.Id);
        Assert.True(persistedConv.IsDisbanded);
        Assert.NotNull(persistedConv.DisbandedAt);

        var joinReq = await context.GroupJoinRequests.FirstAsync(r => r.ConversationId == group.Id && r.UserId == pendingOutsider.Id);
        Assert.Equal(JoinRequestStatus.Rejected, joinReq.Status);
    }

    [Fact]
    public async Task DisbandGroup_BlocksAllFurtherMessagesReactionsPinsPollsAndSettings()
    {
        using var context = TestDbContextFactory.Create(nameof(DisbandGroup_BlocksAllFurtherMessagesReactionsPinsPollsAndSettings));
        var owner = CreateUser("owner");
        var member = CreateUser("member");
        context.Users.AddRange(owner, member);
        await context.SaveChangesAsync();

        var groupService = new GroupChatService(context);
        var chatService = new ChatService(context);
        var interactionService = new ChatInteractionService(context);

        var group = await groupService.CreateGroupAsync(owner.Id, new CreateGroupConversationRequest
        {
            Name = "Temp Group",
            MemberIds = new List<Guid> { member.Id }
        });

        var msg = await chatService.SendMessageAsync(group.Id, owner.Id, new SendMessageRequest { Content = "Before disband" });

        // Non-owner cannot disband
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => groupService.DisbandGroupAsync(group.Id, member.Id));

        // Owner disbands
        var disbanded = await groupService.DisbandGroupAsync(group.Id, owner.Id);
        Assert.Equal("GroupDisbanded", disbanded.EventType);
        Assert.True(disbanded.Conversation!.IsDisbanded);
        Assert.Null(disbanded.Conversation.InviteCode);
        Assert.NotNull(disbanded.SystemMessage);

        // All further write operations are blocked
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            chatService.SendMessageAsync(group.Id, owner.Id, new SendMessageRequest { Content = "After disband" }));
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            chatService.EditMessageAsync(msg.Id, owner.Id, new EditMessageRequest { Content = "Edited after disband" }));
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            interactionService.ToggleReactionAsync(group.Id, msg.Id, owner.Id, new ToggleReactionRequest { Emoji = "👍" }));
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            interactionService.PinMessageAsync(group.Id, msg.Id, owner.Id));
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            interactionService.CreatePollAsync(group.Id, owner.Id, new CreatePollRequest
            {
                Question = "Poll?",
                Options = new List<string> { "A", "B" }
            }));
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            groupService.UpdateGroupSettingsAsync(group.Id, owner.Id, new UpdateGroupSettingsRequest { Name = "Revived" }));

        // History remains viewable in GetConversationsAsync with IsDisbanded = true
        var convList = await chatService.GetConversationsAsync(member.Id, new PaginationParams());
        var fetched = Assert.Single(convList.Items, c => c.Id == group.Id);
        Assert.True(fetched.IsDisbanded);
    }

    // =========================================================================
    // 4. INVITE LINK & JOIN APPROVAL WORKFLOW
    // =========================================================================

    [Fact]
    public async Task InviteLink_DirectJoin_AndRegenerateInvalidatesOldCode()
    {
        using var context = TestDbContextFactory.Create(nameof(InviteLink_DirectJoin_AndRegenerateInvalidatesOldCode));
        var owner = CreateUser("owner");
        var member = CreateUser("member");
        var joiner1 = CreateUser("joiner1");
        var joiner2 = CreateUser("joiner2");
        context.Users.AddRange(owner, member, joiner1, joiner2);
        await context.SaveChangesAsync();

        var groupService = new GroupChatService(context);
        var group = await groupService.CreateGroupAsync(owner.Id, new CreateGroupConversationRequest
        {
            Name = "Open Community",
            MemberIds = new List<Guid> { member.Id },
            RequireJoinApproval = false,
            IsInviteLinkEnabled = true
        });
        var originalCode = group.InviteCode!;

        // Preview before joining
        var previewBefore = await groupService.GetInvitePreviewAsync(originalCode, joiner1.Id);
        Assert.Equal(group.Id, previewBefore.ConversationId);
        Assert.Equal("Open Community", previewBefore.Name);
        Assert.Equal(2, previewBefore.MemberCount);
        Assert.False(previewBefore.RequireJoinApproval);
        Assert.False(previewBefore.IsAlreadyMember);
        Assert.False(previewBefore.HasPendingRequest);

        // Direct join succeeds immediately
        var joined = await groupService.JoinByInviteCodeAsync(originalCode, joiner1.Id);
        Assert.Equal("MembersAdded", joined.EventType);
        Assert.Contains(joined.Conversation!.Members, m => m.UserId == joiner1.Id && m.Role == "Member");
        Assert.NotNull(joined.SystemMessage);

        // Preview after joining marks IsAlreadyMember = true
        var previewAfter = await groupService.GetInvitePreviewAsync(originalCode, joiner1.Id);
        Assert.True(previewAfter.IsAlreadyMember);

        // Regenerate invite code -> old code stops working, new code works
        var newCode = await groupService.RegenerateInviteCodeAsync(group.Id, owner.Id);
        Assert.NotEqual(originalCode, newCode);

        await Assert.ThrowsAsync<KeyNotFoundException>(() => groupService.GetInvitePreviewAsync(originalCode, joiner2.Id));
        await Assert.ThrowsAsync<KeyNotFoundException>(() => groupService.JoinByInviteCodeAsync(originalCode, joiner2.Id));

        var joinedWithNewCode = await groupService.JoinByInviteCodeAsync(newCode, joiner2.Id);
        Assert.Equal("MembersAdded", joinedWithNewCode.EventType);
        Assert.Contains(joinedWithNewCode.Conversation!.Members, m => m.UserId == joiner2.Id);
    }

    [Fact]
    public async Task InviteLink_RequireJoinApproval_PendingApproveAndRejectWorkflow()
    {
        using var context = TestDbContextFactory.Create(nameof(InviteLink_RequireJoinApproval_PendingApproveAndRejectWorkflow));
        var owner = CreateUser("owner");
        var admin = CreateUser("admin");
        var member = CreateUser("member");
        var applicant1 = CreateUser("app1");
        var applicant2 = CreateUser("app2");
        var invitedByMember = CreateUser("inv3");
        context.Users.AddRange(owner, admin, member, applicant1, applicant2, invitedByMember);
        await context.SaveChangesAsync();

        var groupService = new GroupChatService(context);
        var group = await groupService.CreateGroupAsync(owner.Id, new CreateGroupConversationRequest
        {
            Name = "Moderated Club",
            MemberIds = new List<Guid> { admin.Id, member.Id },
            RequireJoinApproval = true,
            IsInviteLinkEnabled = true
        });
        await groupService.UpdateMemberRoleAsync(group.Id, owner.Id, admin.Id, new UpdateMemberRoleRequest { Role = "Admin" });

        // Applicant 1 & 2 join via invite code -> Pending requests (NOT added yet)
        var req1Event = await groupService.JoinByInviteCodeAsync(group.InviteCode!, applicant1.Id);
        Assert.Equal("JoinRequestCreated", req1Event.EventType);
        Assert.NotNull(req1Event.JoinRequest);
        Assert.Equal("Pending", req1Event.JoinRequest!.Status);

        var req2Event = await groupService.JoinByInviteCodeAsync(group.InviteCode!, applicant2.Id);
        Assert.Equal("JoinRequestCreated", req2Event.EventType);

        // Preview reflects HasPendingRequest = true
        var previewPending = await groupService.GetInvitePreviewAsync(group.InviteCode!, applicant1.Id);
        Assert.True(previewPending.HasPendingRequest);
        Assert.False(previewPending.IsAlreadyMember);

        // Regular Member adding someone under RequireJoinApproval also creates pending request
        var memberAddEvent = await groupService.AddMembersAsync(group.Id, member.Id, new AddGroupMembersRequest
        {
            UserIds = new List<Guid> { invitedByMember.Id }
        });
        Assert.Equal("JoinRequestCreated", memberAddEvent.EventType);

        // Regular Member cannot view or review join requests
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => groupService.GetJoinRequestsAsync(group.Id, member.Id));
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() =>
            groupService.ReviewJoinRequestAsync(group.Id, req1Event.JoinRequest.Id, member.Id, new ReviewJoinRequestDto { Approve = true }));

        // Admin views all 3 pending requests
        var pendingList = await groupService.GetJoinRequestsAsync(group.Id, admin.Id);
        Assert.Equal(3, pendingList.Count);

        // Admin approves applicant1 -> added as Member + system message
        var approved = await groupService.ReviewJoinRequestAsync(group.Id, req1Event.JoinRequest.Id, admin.Id, new ReviewJoinRequestDto { Approve = true });
        Assert.Equal("JoinRequestApproved", approved.EventType);
        Assert.Equal("Approved", approved.JoinRequest!.Status);
        Assert.Contains(approved.Conversation!.Members, m => m.UserId == applicant1.Id && m.Role == "Member");
        Assert.NotNull(approved.SystemMessage);

        // Owner rejects applicant2 -> NOT added
        var rejected = await groupService.ReviewJoinRequestAsync(group.Id, req2Event.JoinRequest!.Id, owner.Id, new ReviewJoinRequestDto { Approve = false });
        Assert.Equal("JoinRequestRejected", rejected.EventType);
        Assert.Equal("Rejected", rejected.JoinRequest!.Status);
        Assert.DoesNotContain(rejected.Conversation!.Members, m => m.UserId == applicant2.Id);

        // Re-reviewing already handled request throws InvalidOperationException
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            groupService.ReviewJoinRequestAsync(group.Id, req1Event.JoinRequest.Id, owner.Id, new ReviewJoinRequestDto { Approve = false }));
    }

    // =========================================================================
    // 5. NICKNAME UPDATE & MUTE CONVERSATION
    // =========================================================================

    [Fact]
    public async Task NicknameUpdate_AndMuteConversation_WorkWithProperPermissions()
    {
        using var context = TestDbContextFactory.Create(nameof(NicknameUpdate_AndMuteConversation_WorkWithProperPermissions));
        var owner = CreateUser("owner");
        var member1 = CreateUser("m1");
        var member2 = CreateUser("m2");
        var stranger = CreateUser("stranger");
        context.Users.AddRange(owner, member1, member2, stranger);
        await context.SaveChangesAsync();

        var groupService = new GroupChatService(context);
        var group = await groupService.CreateGroupAsync(owner.Id, new CreateGroupConversationRequest
        {
            Name = "Nicknames & Mute",
            MemberIds = new List<Guid> { member1.Id, member2.Id }
        });

        // Member updates own nickname -> succeeds
        var selfNick = await groupService.UpdateNicknameAsync(group.Id, member1.Id, member1.Id, new UpdateNicknameRequest { Nickname = "Captain M1" });
        Assert.Equal("MemberNicknameUpdated", selfNick.EventType);
        Assert.Equal("Captain M1", selfNick.Conversation!.Members.Single(m => m.UserId == member1.Id).Nickname);
        Assert.NotNull(selfNick.SystemMessage);

        // Member CANNOT update another member's nickname
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() =>
            groupService.UpdateNicknameAsync(group.Id, member1.Id, member2.Id, new UpdateNicknameRequest { Nickname = "Troll" }));

        // Owner CAN update and clear another member's nickname
        var ownerSetNick = await groupService.UpdateNicknameAsync(group.Id, owner.Id, member2.Id, new UpdateNicknameRequest { Nickname = "VIP M2" });
        Assert.Equal("VIP M2", ownerSetNick.Conversation!.Members.Single(m => m.UserId == member2.Id).Nickname);

        var ownerClearNick = await groupService.UpdateNicknameAsync(group.Id, owner.Id, member2.Id, new UpdateNicknameRequest { Nickname = "   " });
        Assert.Null(ownerClearNick.Conversation!.Members.Single(m => m.UserId == member2.Id).Nickname);

        // Mute by DurationMinutes
        var beforeMute = DateTime.UtcNow;
        var mutedUntil1 = await groupService.MuteConversationAsync(group.Id, member1.Id, new MuteConversationRequest { DurationMinutes = 60 });
        Assert.NotNull(mutedUntil1);
        Assert.True(mutedUntil1!.Value >= beforeMute.AddMinutes(59));

        var convForM1 = await groupService.GetConversationByIdAsync(group.Id, member1.Id);
        Assert.Equal(mutedUntil1, convForM1.MutedUntil);

        // Mute by explicit MutedUntil
        var explicitUntil = DateTime.UtcNow.AddHours(8);
        var mutedUntil2 = await groupService.MuteConversationAsync(group.Id, member1.Id, new MuteConversationRequest { MutedUntil = explicitUntil });
        Assert.NotNull(mutedUntil2);
        Assert.True(Math.Abs((mutedUntil2!.Value - explicitUntil).TotalSeconds) < 2);

        // Unmute with DurationMinutes = 0
        var unmuted = await groupService.MuteConversationAsync(group.Id, member1.Id, new MuteConversationRequest { DurationMinutes = 0 });
        Assert.Null(unmuted);

        // Non-member cannot mute
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() =>
            groupService.MuteConversationAsync(group.Id, stranger.Id, new MuteConversationRequest { DurationMinutes = 30 }));
    }

    // =========================================================================
    // 6. INTERACTIONS: REPLY, MENTIONS, REACTIONS, PINS, POLLS
    // =========================================================================

    [Fact]
    public async Task Interactions_ReplyMentionsReactionsAndPins_WorkAndEnforceGuards()
    {
        using var context = TestDbContextFactory.Create(nameof(Interactions_ReplyMentionsReactionsAndPins_WorkAndEnforceGuards));
        var owner = CreateUser("owner");
        var admin = CreateUser("admin");
        var member = CreateUser("member");
        context.Users.AddRange(owner, admin, member);
        await context.SaveChangesAsync();

        var groupService = new GroupChatService(context);
        var chatService = new ChatService(context);
        var interactionService = new ChatInteractionService(context);

        var group1 = await groupService.CreateGroupAsync(owner.Id, new CreateGroupConversationRequest
        {
            Name = "Group One",
            MemberIds = new List<Guid> { admin.Id, member.Id }
        });
        await groupService.UpdateMemberRoleAsync(group1.Id, owner.Id, admin.Id, new UpdateMemberRoleRequest { Role = "Admin" });

        var group2 = await groupService.CreateGroupAsync(owner.Id, new CreateGroupConversationRequest
        {
            Name = "Group Two",
            MemberIds = new List<Guid> { member.Id }
        });

        var msg1 = await chatService.SendMessageAsync(group1.Id, owner.Id, new SendMessageRequest { Content = "Original message in Group 1" });
        var otherConvMsg = await chatService.SendMessageAsync(group2.Id, owner.Id, new SendMessageRequest { Content = "Message in Group 2" });

        // Reply in same conversation + Mentions + MentionAll
        var replyMsg = await chatService.SendMessageAsync(group1.Id, member.Id, new SendMessageRequest
        {
            Content = "@all Replying to owner!",
            ReplyToMessageId = msg1.Id,
            MentionedUserIds = new List<Guid> { owner.Id, admin.Id },
            MentionAll = true
        });
        Assert.Equal(msg1.Id, replyMsg.ReplyToMessageId);
        Assert.NotNull(replyMsg.ReplyTo);
        Assert.Equal("Original message in Group 1", replyMsg.ReplyTo!.Content);
        Assert.Equal(2, replyMsg.MentionedUserIds.Count);
        Assert.Contains(owner.Id, replyMsg.MentionedUserIds);
        Assert.Contains(admin.Id, replyMsg.MentionedUserIds);
        Assert.True(replyMsg.MentionAll);

        // Cross-conversation reply is rejected
        await Assert.ThrowsAsync<KeyNotFoundException>(() =>
            chatService.SendMessageAsync(group1.Id, member.Id, new SendMessageRequest
            {
                Content = "Cross reply",
                ReplyToMessageId = otherConvMsg.Id
            }));

        // Direct chat gates MentionAll to false
        var direct = await chatService.GetOrCreateConversationAsync(owner.Id, member.Id);
        var directMsg = await chatService.SendMessageAsync(direct.Id, owner.Id, new SendMessageRequest
        {
            Content = "Hello direct",
            MentionAll = true
        });
        Assert.False(directMsg.MentionAll);

        // ToggleReaction: add, distinct emojis coexist, toggle off removes
        var react1 = await interactionService.ToggleReactionAsync(group1.Id, msg1.Id, owner.Id, new ToggleReactionRequest { Emoji = "👍" });
        Assert.True(react1.Added);
        Assert.Single(react1.Reactions);
        Assert.Equal(1, react1.Reactions[0].Count);
        Assert.True(react1.Reactions[0].ReactedByCurrentUser);

        var react2 = await interactionService.ToggleReactionAsync(group1.Id, msg1.Id, member.Id, new ToggleReactionRequest { Emoji = "👍" });
        Assert.True(react2.Added);
        Assert.Equal(2, react2.Reactions.Single(r => r.Emoji == "👍").Count);

        var reactHeart = await interactionService.ToggleReactionAsync(group1.Id, msg1.Id, member.Id, new ToggleReactionRequest { Emoji = "❤️" });
        Assert.True(reactHeart.Added);
        Assert.Equal(2, reactHeart.Reactions.Count);
        Assert.Equal(2, reactHeart.Reactions.Single(r => r.Emoji == "👍").Count);
        Assert.Equal(1, reactHeart.Reactions.Single(r => r.Emoji == "❤️").Count);

        var unreact1 = await interactionService.ToggleReactionAsync(group1.Id, msg1.Id, owner.Id, new ToggleReactionRequest { Emoji = "👍" });
        Assert.False(unreact1.Added);
        var thumbAfter = unreact1.Reactions.Single(r => r.Emoji == "👍");
        Assert.Equal(1, thumbAfter.Count);
        Assert.False(thumbAfter.ReactedByCurrentUser);

        // Pin / Unpin RBAC in Group: Member blocked, Owner & Admin allowed
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() =>
            interactionService.PinMessageAsync(group1.Id, msg1.Id, member.Id));

        var pinned1 = await interactionService.PinMessageAsync(group1.Id, msg1.Id, owner.Id);
        Assert.True(pinned1.IsPinned);
        Assert.Equal(owner.Id, pinned1.PinnedById);

        await Task.Delay(15);
        var pinned2 = await interactionService.PinMessageAsync(group1.Id, replyMsg.Id, admin.Id);
        Assert.True(pinned2.IsPinned);

        var pinnedList = await interactionService.GetPinnedMessagesAsync(group1.Id, member.Id);
        Assert.Equal(2, pinnedList.Count);
        Assert.Equal(replyMsg.Id, pinnedList[0].Id); // Ordered by PinnedAt DESC
        Assert.Equal(msg1.Id, pinnedList[1].Id);

        // Member cannot unpin
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() =>
            interactionService.UnpinMessageAsync(group1.Id, msg1.Id, member.Id));

        var unpinned = await interactionService.UnpinMessageAsync(group1.Id, msg1.Id, owner.Id);
        Assert.False(unpinned.IsPinned);
        Assert.Single(await interactionService.GetPinnedMessagesAsync(group1.Id, member.Id));

        // Direct chat allows either participant to pin & unpin
        var directPin = await interactionService.PinMessageAsync(direct.Id, directMsg.Id, member.Id);
        Assert.True(directPin.IsPinned);
    }

    [Fact]
    public async Task Interactions_Polls_SingleChoice_MultiChoice_Anonymous_AndClose()
    {
        using var context = TestDbContextFactory.Create(nameof(Interactions_Polls_SingleChoice_MultiChoice_Anonymous_AndClose));
        var owner = CreateUser("owner");
        var member1 = CreateUser("m1");
        var member2 = CreateUser("m2");
        context.Users.AddRange(owner, member1, member2);
        await context.SaveChangesAsync();

        var groupService = new GroupChatService(context);
        var interactionService = new ChatInteractionService(context);

        var group = await groupService.CreateGroupAsync(owner.Id, new CreateGroupConversationRequest
        {
            Name = "Poll Room",
            MemberIds = new List<Guid> { member1.Id, member2.Id }
        });

        // 1) Single-choice non-anonymous poll
        var singlePollMsg = await interactionService.CreatePollAsync(group.Id, member1.Id, new CreatePollRequest
        {
            Question = "Lunch today?",
            Options = new List<string> { "Pho", "Bun Cha", "Com Tam" },
            AllowMultipleChoices = false,
            IsAnonymous = false,
            ClientMessageId = "poll-single-1"
        });
        Assert.Equal("Poll", singlePollMsg.Type);
        Assert.NotNull(singlePollMsg.Poll);
        Assert.Equal(3, singlePollMsg.Poll!.Options.Count);

        var singlePollId = singlePollMsg.Poll.Id;
        var opt0 = singlePollMsg.Poll.Options[0].Id;
        var opt1 = singlePollMsg.Poll.Options[1].Id;

        // Voting 2 options on single-choice poll is rejected
        await Assert.ThrowsAsync<ArgumentException>(() =>
            interactionService.VotePollAsync(group.Id, singlePollId, member2.Id, new VotePollRequest
            {
                OptionIds = new List<Guid> { opt0, opt1 }
            }));

        // Vote option 0 -> VoterIds visible
        var vote1 = await interactionService.VotePollAsync(group.Id, singlePollId, member2.Id, new VotePollRequest
        {
            OptionIds = new List<Guid> { opt0 }
        });
        Assert.Equal(1, vote1.Poll.TotalVotes);
        Assert.Equal(1, vote1.Poll.TotalVoters);
        Assert.Contains(member2.Id, vote1.Poll.Options.Single(o => o.Id == opt0).VoterIds);
        Assert.True(vote1.Poll.Options.Single(o => o.Id == opt0).VotedByCurrentUser);

        // Change vote to option 1 -> replaces previous vote
        var vote2 = await interactionService.VotePollAsync(group.Id, singlePollId, member2.Id, new VotePollRequest
        {
            OptionIds = new List<Guid> { opt1 }
        });
        Assert.Equal(0, vote2.Poll.Options.Single(o => o.Id == opt0).VoteCount);
        Assert.Equal(1, vote2.Poll.Options.Single(o => o.Id == opt1).VoteCount);

        // Non-creator regular member (member2) cannot close member1's poll
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() =>
            interactionService.ClosePollAsync(group.Id, singlePollId, member2.Id));

        // Poll creator (member1) closes poll -> subsequent votes rejected
        var closed = await interactionService.ClosePollAsync(group.Id, singlePollId, member1.Id);
        Assert.True(closed.Poll.IsClosed);
        Assert.NotNull(closed.Poll.ClosedAt);

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            interactionService.VotePollAsync(group.Id, singlePollId, owner.Id, new VotePollRequest { OptionIds = new List<Guid> { opt0 } }));

        // 2) Multi-choice anonymous poll
        var anonPollMsg = await interactionService.CreatePollAsync(group.Id, owner.Id, new CreatePollRequest
        {
            Question = "Favorite stacks?",
            Options = new List<string> { "C#", "React", "SQL" },
            AllowMultipleChoices = true,
            IsAnonymous = true
        });
        var anonPollId = anonPollMsg.Poll!.Id;
        var a0 = anonPollMsg.Poll.Options[0].Id;
        var a1 = anonPollMsg.Poll.Options[1].Id;

        var multiVote = await interactionService.VotePollAsync(group.Id, anonPollId, member1.Id, new VotePollRequest
        {
            OptionIds = new List<Guid> { a0, a1 }
        });
        Assert.Equal(2, multiVote.Poll.TotalVotes);
        Assert.Equal(1, multiVote.Poll.TotalVoters);
        Assert.All(multiVote.Poll.Options, o => Assert.Empty(o.VoterIds)); // Anonymous hides VoterIds!
        Assert.True(multiVote.Poll.Options.Single(o => o.Id == a0).VotedByCurrentUser);
        Assert.True(multiVote.Poll.Options.Single(o => o.Id == a1).VotedByCurrentUser);

        // Another user inspecting anonymous poll sees vote counts, empty VoterIds, and VotedByCurrentUser == false
        var fetchedForM2 = await interactionService.GetPollAsync(group.Id, anonPollId, member2.Id);
        Assert.Equal(2, fetchedForM2.TotalVotes);
        Assert.All(fetchedForM2.Options, o =>
        {
            Assert.Empty(o.VoterIds);
            Assert.False(o.VotedByCurrentUser);
        });

        // Empty OptionIds clears user's existing votes (unvote)
        var unvoted = await interactionService.VotePollAsync(group.Id, anonPollId, member1.Id, new VotePollRequest
        {
            OptionIds = new List<Guid>()
        });
        Assert.Equal(0, unvoted.Poll.TotalVotes);
        Assert.Equal(0, unvoted.Poll.TotalVoters);
    }

    [Fact]
    public async Task GroupModeration_AdminCanDeleteMemberMessageAndNonEditableTypesBlocked()
    {
        using var context = TestDbContextFactory.Create(nameof(GroupModeration_AdminCanDeleteMemberMessageAndNonEditableTypesBlocked));
        var owner = CreateUser("owner");
        var admin = CreateUser("admin");
        var member1 = CreateUser("m1");
        var member2 = CreateUser("m2");
        context.Users.AddRange(owner, admin, member1, member2);
        await context.SaveChangesAsync();

        var groupService = new GroupChatService(context);
        var chatService = new ChatService(context);
        var interactionService = new ChatInteractionService(context);

        var group = await groupService.CreateGroupAsync(owner.Id, new CreateGroupConversationRequest
        {
            Name = "Moderation Group",
            MemberIds = new List<Guid> { admin.Id, member1.Id, member2.Id }
        });
        await groupService.UpdateMemberRoleAsync(group.Id, owner.Id, admin.Id, new UpdateMemberRoleRequest { Role = "Admin" });

        // System message cannot be edited
        var sysMsgId = group.LastMessage!.Id;
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            chatService.EditMessageAsync(sysMsgId, owner.Id, new EditMessageRequest { Content = "Edited sys" }));

        // Poll message cannot be edited
        var pollMsg = await interactionService.CreatePollAsync(group.Id, member1.Id, new CreatePollRequest
        {
            Question = "Immutable poll?",
            Options = new List<string> { "A", "B" }
        });
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            chatService.EditMessageAsync(pollMsg.Id, member1.Id, new EditMessageRequest { Content = "Edited poll" }));

        // Member1 sends & Owner pins -> Member2 cannot delete, Admin CAN delete and pin flag is cleared
        var m1Msg = await chatService.SendMessageAsync(group.Id, member1.Id, new SendMessageRequest { Content = "Spam" });
        await interactionService.PinMessageAsync(group.Id, m1Msg.Id, owner.Id);

        await Assert.ThrowsAsync<UnauthorizedAccessException>(() =>
            chatService.DeleteMessageAsync(m1Msg.Id, member2.Id));

        var deleted = await chatService.DeleteMessageAsync(m1Msg.Id, admin.Id);
        Assert.Equal(m1Msg.Id, deleted.MessageId);

        var pinnedAfterDelete = await interactionService.GetPinnedMessagesAsync(group.Id, owner.Id);
        Assert.Empty(pinnedAfterDelete);
    }

    // =========================================================================
    // 7. END-TO-END HTTP API INTEGRATION TESTS (SQL Server + Controllers)
    // =========================================================================

    [Fact]
    public async Task Api_GroupLifecycle_Rbac_InviteApproval_MuteAndDisband_EndToEnd()
    {
        var owner = await CreateAuthenticatedApiUserAsync("grpown");
        var admin = await CreateAuthenticatedApiUserAsync("grpadm");
        var member = await CreateAuthenticatedApiUserAsync("grpmem");
        var outsider = await CreateAuthenticatedApiUserAsync("grpout");
        using var ownerClient = owner.Client;
        using var adminClient = admin.Client;
        using var memberClient = member.Client;
        using var outsiderClient = outsider.Client;

        // 1. Create group
        var createRes = await ownerClient.PostAsJsonAsync("/api/conversations/groups", new CreateGroupConversationRequest
        {
            Name = "Zalo Pro HTTP Group",
            MemberIds = new List<Guid> { admin.UserId, member.UserId },
            IsInviteLinkEnabled = true
        });
        Assert.Equal(HttpStatusCode.Created, createRes.StatusCode);
        var group = (await createRes.Content.ReadFromJsonAsync<ApiResponse<ConversationDto>>())!.Data!;
        Assert.Equal("Group", group.Type);
        Assert.NotNull(group.InviteCode);

        // 2. Promote admin
        var promoteRes = await ownerClient.PutAsJsonAsync(
            $"/api/conversations/{group.Id}/members/{admin.UserId}/role",
            new UpdateMemberRoleRequest { Role = "Admin" });
        Assert.Equal(HttpStatusCode.OK, promoteRes.StatusCode);

        // 3. Member cannot update group settings -> 403 Forbidden
        var memberSettingsRes = await memberClient.PutAsJsonAsync(
            $"/api/conversations/{group.Id}/group",
            new UpdateGroupSettingsRequest { Name = "Unauthorized Rename" });
        Assert.Equal(HttpStatusCode.Forbidden, memberSettingsRes.StatusCode);

        // 4. Owner enables OnlyAdminsCanSend + RequireJoinApproval
        var ownerSettingsRes = await ownerClient.PutAsJsonAsync(
            $"/api/conversations/{group.Id}/group",
            new UpdateGroupSettingsRequest { OnlyAdminsCanSend = true, RequireJoinApproval = true });
        Assert.Equal(HttpStatusCode.OK, ownerSettingsRes.StatusCode);

        // 5. OnlyAdminsCanSend blocks member (403), allows admin (201)
        var memberSendRes = await memberClient.PostAsJsonAsync(
            $"/api/conversations/{group.Id}/messages",
            new SendMessageRequest { Content = "Blocked msg" });
        Assert.Equal(HttpStatusCode.Forbidden, memberSendRes.StatusCode);

        var adminSendRes = await adminClient.PostAsJsonAsync(
            $"/api/conversations/{group.Id}/messages",
            new SendMessageRequest { Content = "Admin allowed msg" });
        Assert.Equal(HttpStatusCode.Created, adminSendRes.StatusCode);

        // 6. Invite preview + join approval workflow
        var oldCode = group.InviteCode!;
        var previewRes = await outsiderClient.GetAsync($"/api/conversations/invite/{oldCode}");
        Assert.Equal(HttpStatusCode.OK, previewRes.StatusCode);
        var preview = (await previewRes.Content.ReadFromJsonAsync<ApiResponse<GroupInvitePreviewDto>>())!.Data!;
        Assert.True(preview.RequireJoinApproval);
        Assert.False(preview.IsAlreadyMember);

        var joinRes = await outsiderClient.PostAsync($"/api/conversations/invite/{oldCode}/join", null);
        Assert.Equal(HttpStatusCode.OK, joinRes.StatusCode);
        var joinEvent = (await joinRes.Content.ReadFromJsonAsync<ApiResponse<GroupEventDto>>())!.Data!;
        Assert.Equal("JoinRequestCreated", joinEvent.EventType);

        var reqsRes = await adminClient.GetAsync($"/api/conversations/{group.Id}/join-requests");
        Assert.Equal(HttpStatusCode.OK, reqsRes.StatusCode);
        var reqs = (await reqsRes.Content.ReadFromJsonAsync<ApiResponse<List<GroupJoinRequestDto>>>())!.Data!;
        var pendingReq = Assert.Single(reqs, r => r.UserId == outsider.UserId);

        var approveRes = await adminClient.PostAsJsonAsync(
            $"/api/conversations/{group.Id}/join-requests/{pendingReq.Id}/review",
            new ReviewJoinRequestDto { Approve = true });
        Assert.Equal(HttpStatusCode.OK, approveRes.StatusCode);

        // 7. Reset invite link -> old code returns 404
        var resetRes = await ownerClient.PostAsync($"/api/conversations/{group.Id}/invite-link/reset", null);
        Assert.Equal(HttpStatusCode.OK, resetRes.StatusCode);
        var newCodeResp = (await resetRes.Content.ReadFromJsonAsync<ApiResponse<InviteCodeResponse>>())!.Data!;
        Assert.NotEqual(oldCode, newCodeResp.InviteCode);

        var oldPreviewRes = await outsiderClient.GetAsync($"/api/conversations/invite/{oldCode}");
        Assert.Equal(HttpStatusCode.NotFound, oldPreviewRes.StatusCode);

        // 8. Nickname + Mute
        var nickRes = await memberClient.PutAsJsonAsync(
            $"/api/conversations/{group.Id}/members/{member.UserId}/nickname",
            new UpdateNicknameRequest { Nickname = "API Member" });
        Assert.Equal(HttpStatusCode.OK, nickRes.StatusCode);

        var muteRes = await memberClient.PutAsJsonAsync(
            $"/api/conversations/{group.Id}/mute",
            new MuteConversationRequest { DurationMinutes = 120 });
        Assert.Equal(HttpStatusCode.OK, muteRes.StatusCode);
        var mutedUntil = (await muteRes.Content.ReadFromJsonAsync<ApiResponse<DateTime?>>())!.Data;
        Assert.NotNull(mutedUntil);

        // 9. Owner cannot leave without NewOwnerUserId -> 400 BadRequest
        var badLeaveRes = await ownerClient.PostAsync($"/api/conversations/{group.Id}/leave", null);
        Assert.Equal(HttpStatusCode.BadRequest, badLeaveRes.StatusCode);

        // 10. Disband group -> blocks further messages (400 BadRequest)
        var disbandRes = await ownerClient.DeleteAsync($"/api/conversations/{group.Id}/disband");
        Assert.Equal(HttpStatusCode.OK, disbandRes.StatusCode);

        var afterDisbandSend = await ownerClient.PostAsJsonAsync(
            $"/api/conversations/{group.Id}/messages",
            new SendMessageRequest { Content = "Should fail" });
        Assert.Equal(HttpStatusCode.BadRequest, afterDisbandSend.StatusCode);
    }

    [Fact]
    public async Task Api_GroupInteractions_ReplyReactionsPinsAndPolls_EndToEnd()
    {
        var owner = await CreateAuthenticatedApiUserAsync("intown");
        var member = await CreateAuthenticatedApiUserAsync("intmem");
        using var ownerClient = owner.Client;
        using var memberClient = member.Client;

        var createRes = await ownerClient.PostAsJsonAsync("/api/conversations/groups", new CreateGroupConversationRequest
        {
            Name = "Interactions Group",
            MemberIds = new List<Guid> { member.UserId }
        });
        createRes.EnsureSuccessStatusCode();
        var group = (await createRes.Content.ReadFromJsonAsync<ApiResponse<ConversationDto>>())!.Data!;

        // Send message + reply with mentions
        var msg1Res = await ownerClient.PostAsJsonAsync($"/api/conversations/{group.Id}/messages", new SendMessageRequest
        {
            Content = "Base message"
        });
        msg1Res.EnsureSuccessStatusCode();
        var msg1 = (await msg1Res.Content.ReadFromJsonAsync<ApiResponse<MessageDto>>())!.Data!;

        var replyRes = await memberClient.PostAsJsonAsync($"/api/conversations/{group.Id}/messages", new SendMessageRequest
        {
            Content = "@all check this",
            ReplyToMessageId = msg1.Id,
            MentionedUserIds = new List<Guid> { owner.UserId },
            MentionAll = true
        });
        Assert.Equal(HttpStatusCode.Created, replyRes.StatusCode);
        var reply = (await replyRes.Content.ReadFromJsonAsync<ApiResponse<MessageDto>>())!.Data!;
        Assert.Equal(msg1.Id, reply.ReplyToMessageId);
        Assert.True(reply.MentionAll);

        // Distinct emojis on SQL Server (verifies Latin1_General_100_BIN2 collation!)
        var r1 = await memberClient.PostAsJsonAsync(
            $"/api/conversations/{group.Id}/messages/{msg1.Id}/reactions",
            new ToggleReactionRequest { Emoji = "👍" });
        Assert.Equal(HttpStatusCode.OK, r1.StatusCode);

        var r2 = await memberClient.PostAsJsonAsync(
            $"/api/conversations/{group.Id}/messages/{msg1.Id}/reactions",
            new ToggleReactionRequest { Emoji = "❤️" });
        Assert.Equal(HttpStatusCode.OK, r2.StatusCode);
        var r2Body = (await r2.Content.ReadFromJsonAsync<ApiResponse<MessageReactionEventDto>>())!.Data!;
        Assert.Equal(2, r2Body.Reactions.Count);

        // Toggle off 👍
        var r3 = await memberClient.PostAsJsonAsync(
            $"/api/conversations/{group.Id}/messages/{msg1.Id}/reactions",
            new ToggleReactionRequest { Emoji = "👍" });
        var r3Body = (await r3.Content.ReadFromJsonAsync<ApiResponse<MessageReactionEventDto>>())!.Data!;
        Assert.False(r3Body.Added);
        Assert.Single(r3Body.Reactions);

        // Pin RBAC: Member 403, Owner 200
        var memberPinRes = await memberClient.PostAsync($"/api/conversations/{group.Id}/messages/{msg1.Id}/pin", null);
        Assert.Equal(HttpStatusCode.Forbidden, memberPinRes.StatusCode);

        var ownerPinRes = await ownerClient.PostAsync($"/api/conversations/{group.Id}/messages/{msg1.Id}/pin", null);
        Assert.Equal(HttpStatusCode.OK, ownerPinRes.StatusCode);

        var pinnedListRes = await memberClient.GetFromJsonAsync<ApiResponse<List<MessageDto>>>($"/api/conversations/{group.Id}/pinned-messages");
        Assert.Single(pinnedListRes!.Data!, m => m.Id == msg1.Id);

        var unpinRes = await ownerClient.DeleteAsync($"/api/conversations/{group.Id}/messages/{msg1.Id}/pin");
        Assert.Equal(HttpStatusCode.OK, unpinRes.StatusCode);

        // Poll create -> vote -> get -> close -> vote rejected
        var pollCreateRes = await ownerClient.PostAsJsonAsync($"/api/conversations/{group.Id}/polls", new CreatePollRequest
        {
            Question = "Deploy tonight?",
            Options = new List<string> { "Ship it", "Wait" },
            AllowMultipleChoices = false,
            IsAnonymous = true
        });
        Assert.Equal(HttpStatusCode.Created, pollCreateRes.StatusCode);
        var pollMsg = (await pollCreateRes.Content.ReadFromJsonAsync<ApiResponse<MessageDto>>())!.Data!;
        var pollId = pollMsg.Poll!.Id;
        var opt0 = pollMsg.Poll.Options[0].Id;

        var voteRes = await memberClient.PostAsJsonAsync(
            $"/api/conversations/{group.Id}/polls/{pollId}/vote",
            new VotePollRequest { OptionIds = new List<Guid> { opt0 } });
        Assert.Equal(HttpStatusCode.OK, voteRes.StatusCode);
        var voteData = (await voteRes.Content.ReadFromJsonAsync<ApiResponse<PollUpdatedEventDto>>())!.Data!;
        Assert.Equal(1, voteData.Poll.TotalVotes);
        Assert.Empty(voteData.Poll.Options[0].VoterIds); // Anonymous

        var getPollRes = await ownerClient.GetFromJsonAsync<ApiResponse<ChatPollDto>>($"/api/conversations/{group.Id}/polls/{pollId}");
        Assert.Equal(1, getPollRes!.Data!.TotalVotes);
        Assert.Empty(getPollRes.Data.Options[0].VoterIds);

        var closeRes = await ownerClient.PostAsync($"/api/conversations/{group.Id}/polls/{pollId}/close", null);
        Assert.Equal(HttpStatusCode.OK, closeRes.StatusCode);

        var voteAfterCloseRes = await memberClient.PostAsJsonAsync(
            $"/api/conversations/{group.Id}/polls/{pollId}/vote",
            new VotePollRequest { OptionIds = new List<Guid> { opt0 } });
        Assert.Equal(HttpStatusCode.BadRequest, voteAfterCloseRes.StatusCode);
    }
}
