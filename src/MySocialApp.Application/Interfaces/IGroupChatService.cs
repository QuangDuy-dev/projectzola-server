using MySocialApp.Application.DTOs.Chat;

namespace MySocialApp.Application.Interfaces;

public interface IGroupChatService
{
    Task<ConversationDto> CreateGroupAsync(Guid creatorId, CreateGroupConversationRequest request, CancellationToken ct = default);
    Task<ConversationDto> GetConversationByIdAsync(Guid conversationId, Guid userId, CancellationToken ct = default);
    Task<GroupEventDto> UpdateGroupSettingsAsync(Guid conversationId, Guid actorUserId, UpdateGroupSettingsRequest request, CancellationToken ct = default);
    Task<GroupEventDto> UpdateGroupAvatarAsync(Guid conversationId, Guid actorUserId, Stream fileStream, string fileName, string contentType, CancellationToken ct = default);

    Task<GroupEventDto> AddMembersAsync(Guid conversationId, Guid actorUserId, AddGroupMembersRequest request, CancellationToken ct = default);
    Task<GroupEventDto> RemoveMemberAsync(Guid conversationId, Guid actorUserId, Guid targetUserId, CancellationToken ct = default);
    Task<GroupEventDto> UpdateMemberRoleAsync(Guid conversationId, Guid actorUserId, Guid targetUserId, UpdateMemberRoleRequest request, CancellationToken ct = default);
    Task<GroupEventDto> UpdateNicknameAsync(Guid conversationId, Guid actorUserId, Guid targetUserId, UpdateNicknameRequest request, CancellationToken ct = default);
    Task<GroupEventDto> TransferOwnershipAsync(Guid conversationId, Guid currentOwnerId, TransferOwnershipRequest request, CancellationToken ct = default);
    Task<GroupEventDto> LeaveGroupAsync(Guid conversationId, Guid userId, LeaveGroupRequest? request = null, CancellationToken ct = default);
    Task<GroupEventDto> DisbandGroupAsync(Guid conversationId, Guid ownerUserId, CancellationToken ct = default);
    Task<DateTime?> MuteConversationAsync(Guid conversationId, Guid userId, MuteConversationRequest request, CancellationToken ct = default);

    Task<string> RegenerateInviteCodeAsync(Guid conversationId, Guid actorUserId, CancellationToken ct = default);
    Task<GroupInvitePreviewDto> GetInvitePreviewAsync(string inviteCode, Guid userId, CancellationToken ct = default);
    Task<GroupEventDto> JoinByInviteCodeAsync(string inviteCode, Guid userId, CancellationToken ct = default);
    Task<IReadOnlyList<GroupJoinRequestDto>> GetJoinRequestsAsync(Guid conversationId, Guid actorUserId, CancellationToken ct = default);
    Task<GroupEventDto> ReviewJoinRequestAsync(Guid conversationId, Guid requestId, Guid actorUserId, ReviewJoinRequestDto request, CancellationToken ct = default);
}
