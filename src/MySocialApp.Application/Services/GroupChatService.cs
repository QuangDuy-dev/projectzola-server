using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using MySocialApp.Application.DTOs.Chat;
using MySocialApp.Application.Interfaces;
using MySocialApp.Domain.Entities;
using MySocialApp.Domain.Enums;

namespace MySocialApp.Application.Services;

public class GroupChatService : IGroupChatService
{
    private const string InviteCodeAlphabet = "ABCDEFGHJKLMNPQRSTUVWXYZabcdefghijkmnopqrstuvwxyz23456789";
    private const int InviteCodeLength = 12;

    private readonly IApplicationDbContext _context;
    private readonly IUserPresenceService? _presenceService;
    private readonly IFileValidator? _fileValidator;
    private readonly IImageProcessingService? _imageProcessingService;
    private readonly IMediaStorageService? _mediaStorageService;

    public GroupChatService(
        IApplicationDbContext context,
        IUserPresenceService? presenceService = null,
        IFileValidator? fileValidator = null,
        IImageProcessingService? imageProcessingService = null,
        IMediaStorageService? mediaStorageService = null)
    {
        _context = context;
        _presenceService = presenceService;
        _fileValidator = fileValidator;
        _imageProcessingService = imageProcessingService;
        _mediaStorageService = mediaStorageService;
    }

    public async Task<ConversationDto> CreateGroupAsync(
        Guid creatorId,
        CreateGroupConversationRequest request,
        CancellationToken ct = default)
    {
        if (request == null)
            throw new ArgumentException("Dữ liệu tạo nhóm không hợp lệ.");

        var trimmedName = request.Name?.Trim() ?? string.Empty;
        if (trimmedName.Length < 1 || trimmedName.Length > 100)
            throw new ArgumentException("Tên nhóm phải từ 1 đến 100 ký tự.");

        var creator = await _context.Users.FirstOrDefaultAsync(u => u.Id == creatorId, ct)
            ?? throw new KeyNotFoundException("Không tìm thấy người tạo nhóm.");

        if (!creator.IsActive)
            throw new InvalidOperationException("Tài khoản của bạn không còn hoạt động.");

        var candidateMemberIds = (request.MemberIds ?? new List<Guid>())
            .Where(id => id != Guid.Empty && id != creatorId)
            .Distinct()
            .ToList();

        if (candidateMemberIds.Count == 0)
            throw new ArgumentException("Nhóm cần có ít nhất 1 thành viên khác ngoài bạn.");

        var activeMembers = await _context.Users
            .Where(u => candidateMemberIds.Contains(u.Id) && u.IsActive)
            .ToListAsync(ct);

        if (activeMembers.Count == 0)
            throw new ArgumentException("Không tìm thấy thành viên hợp lệ nào để tạo nhóm.");

        var now = DateTime.UtcNow;
        var inviteCode = await GenerateUniqueInviteCodeAsync(ct);

        var conversation = new Conversation
        {
            Type = ConversationType.Group,
            Name = trimmedName,
            CreatedById = creatorId,
            InviteCode = inviteCode,
            IsInviteLinkEnabled = request.IsInviteLinkEnabled,
            RequireJoinApproval = request.RequireJoinApproval,
            OnlyAdminsCanSend = request.OnlyAdminsCanSend,
            IsDisbanded = false,
            CreatedAt = now,
            UpdatedAt = now
        };

        conversation.Members.Add(new ConversationMember
        {
            ConversationId = conversation.Id,
            UserId = creatorId,
            Role = ConversationMemberRole.Owner,
            JoinedAt = now
        });

        foreach (var memberUser in activeMembers)
        {
            conversation.Members.Add(new ConversationMember
            {
                ConversationId = conversation.Id,
                UserId = memberUser.Id,
                Role = ConversationMemberRole.Member,
                JoinedAt = now
            });
        }

        _context.Conversations.Add(conversation);

        var sysMessage = new Message
        {
            ConversationId = conversation.Id,
            SenderId = creatorId,
            Type = MessageType.System,
            Content = $"{GetDisplayLabel(creator)} đã tạo nhóm \"{trimmedName}\"",
            CreatedAt = now,
            UpdatedAt = now
        };
        _context.Messages.Add(sysMessage);

        await _context.SaveChangesAsync(ct);

        return await BuildConversationDtoAsync(conversation.Id, creatorId, ct);
    }

    public async Task<ConversationDto> GetConversationByIdAsync(
        Guid conversationId,
        Guid userId,
        CancellationToken ct = default)
    {
        var exists = await _context.Conversations
            .AsNoTracking()
            .AnyAsync(c => c.Id == conversationId, ct);

        if (!exists)
            throw new KeyNotFoundException("Không tìm thấy cuộc trò chuyện.");

        var isMember = await _context.ConversationMembers
            .AsNoTracking()
            .AnyAsync(cm => cm.ConversationId == conversationId && cm.UserId == userId, ct);

        if (!isMember)
            throw new UnauthorizedAccessException("Bạn không phải là thành viên của cuộc trò chuyện này.");

        return await BuildConversationDtoAsync(conversationId, userId, ct);
    }

    public async Task<GroupEventDto> UpdateGroupSettingsAsync(
        Guid conversationId,
        Guid actorUserId,
        UpdateGroupSettingsRequest request,
        CancellationToken ct = default)
    {
        if (request == null)
            throw new ArgumentException("Dữ liệu cập nhật nhóm không hợp lệ.");

        var (conv, actorMember) = await LoadGroupForActorAsync(conversationId, actorUserId, requireNotDisbanded: true, ct);
        EnsureOwnerOrAdmin(actorMember, "Chỉ trưởng nhóm hoặc phó nhóm mới có quyền chỉnh sửa cài đặt nhóm.");

        var changes = new List<string>();
        var now = DateTime.UtcNow;

        if (request.Name != null)
        {
            var trimmedName = request.Name.Trim();
            if (trimmedName.Length < 1 || trimmedName.Length > 100)
                throw new ArgumentException("Tên nhóm phải từ 1 đến 100 ký tự.");

            if (!string.Equals(conv.Name, trimmedName, StringComparison.Ordinal))
            {
                conv.Name = trimmedName;
                changes.Add($"đã đổi tên nhóm thành \"{trimmedName}\"");
            }
        }

        if (request.OnlyAdminsCanSend.HasValue && request.OnlyAdminsCanSend.Value != conv.OnlyAdminsCanSend)
        {
            conv.OnlyAdminsCanSend = request.OnlyAdminsCanSend.Value;
            changes.Add(request.OnlyAdminsCanSend.Value
                ? "đã bật chế độ chỉ trưởng/phó nhóm được gửi tin nhắn"
                : "đã tắt chế độ chỉ trưởng/phó nhóm được gửi tin nhắn");
        }

        if (request.RequireJoinApproval.HasValue && request.RequireJoinApproval.Value != conv.RequireJoinApproval)
        {
            conv.RequireJoinApproval = request.RequireJoinApproval.Value;
            changes.Add(request.RequireJoinApproval.Value
                ? "đã bật phê duyệt thành viên mới"
                : "đã tắt phê duyệt thành viên mới");
        }

        if (request.IsInviteLinkEnabled.HasValue && request.IsInviteLinkEnabled.Value != conv.IsInviteLinkEnabled)
        {
            conv.IsInviteLinkEnabled = request.IsInviteLinkEnabled.Value;
            if (conv.IsInviteLinkEnabled && string.IsNullOrWhiteSpace(conv.InviteCode))
            {
                conv.InviteCode = await GenerateUniqueInviteCodeAsync(ct);
            }

            changes.Add(request.IsInviteLinkEnabled.Value
                ? "đã bật liên kết tham gia nhóm"
                : "đã tắt liên kết tham gia nhóm");
        }

        MessageDto? sysMsgDto = null;
        if (changes.Count > 0)
        {
            var sysMsg = CreateSystemMessage(
                conv,
                actorUserId,
                $"{GetDisplayLabel(actorMember.User)} {string.Join(", ", changes)}",
                now);
            await _context.SaveChangesAsync(ct);
            sysMsgDto = MapSystemMessageToDto(sysMsg, actorMember.User);
        }

        return new GroupEventDto
        {
            ConversationId = conv.Id,
            EventType = "GroupSettingsUpdated",
            ActorUserId = actorUserId,
            Conversation = await BuildConversationDtoAsync(conv.Id, actorUserId, ct),
            SystemMessage = sysMsgDto,
            Timestamp = now
        };
    }

    public async Task<GroupEventDto> UpdateGroupAvatarAsync(
        Guid conversationId,
        Guid actorUserId,
        Stream fileStream,
        string fileName,
        string contentType,
        CancellationToken ct = default)
    {
        if (_mediaStorageService == null || _fileValidator == null)
            throw new InvalidOperationException("Dịch vụ lưu trữ media chưa được cấu hình.");

        var (conv, actorMember) = await LoadGroupForActorAsync(conversationId, actorUserId, requireNotDisbanded: true, ct);
        EnsureOwnerOrAdmin(actorMember, "Chỉ trưởng nhóm hoặc phó nhóm mới có quyền đổi ảnh đại diện nhóm.");

        var validation = _fileValidator.Validate(fileStream, fileName, contentType, AllowedMediaType.Image);
        if (!validation.IsValid)
            throw new ArgumentException(validation.ErrorMessage ?? "Ảnh đại diện nhóm không hợp lệ.");

        var oldAvatarUrl = conv.AvatarUrl;
        Stream uploadStream = fileStream;
        string uploadContentType = validation.NormalizedContentType ?? contentType;
        string extension = validation.DetectedExtension ?? Path.GetExtension(fileName).ToLowerInvariant();

        ProcessedImageResult? processed = null;
        if (_imageProcessingService != null)
        {
            processed = await _imageProcessingService.ProcessAvatarAsync(fileStream, ct);
            uploadStream = processed.Stream;
            uploadContentType = processed.ContentType;
            extension = processed.Extension;
        }

        var key = $"chat/group-avatars/{conversationId}_{Guid.NewGuid():N}{extension}";

        try
        {
            await _mediaStorageService.UploadAsync(uploadStream, key, uploadContentType, ct);
        }
        finally
        {
            processed?.Stream.Dispose();
        }

        var now = DateTime.UtcNow;
        Message sysMsg;
        try
        {
            conv.AvatarUrl = _mediaStorageService.GetUrl(key);
            sysMsg = CreateSystemMessage(
                conv,
                actorUserId,
                $"{GetDisplayLabel(actorMember.User)} đã cập nhật ảnh đại diện nhóm",
                now);
            await _context.SaveChangesAsync(ct);
        }
        catch
        {
            await _mediaStorageService.DeleteAsync(key, ct);
            throw;
        }

        if (!string.IsNullOrWhiteSpace(oldAvatarUrl))
        {
            try
            {
                var oldKey = _mediaStorageService.GetKeyFromUrl(oldAvatarUrl);
                if (!string.IsNullOrWhiteSpace(oldKey))
                {
                    await _mediaStorageService.DeleteAsync(oldKey, ct);
                }
            }
            catch
            {
                // Best-effort cleanup of previous avatar file
            }
        }

        return new GroupEventDto
        {
            ConversationId = conv.Id,
            EventType = "GroupAvatarUpdated",
            ActorUserId = actorUserId,
            Conversation = await BuildConversationDtoAsync(conv.Id, actorUserId, ct),
            SystemMessage = MapSystemMessageToDto(sysMsg, actorMember.User),
            Timestamp = now
        };
    }

    public async Task<GroupEventDto> AddMembersAsync(
        Guid conversationId,
        Guid actorUserId,
        AddGroupMembersRequest request,
        CancellationToken ct = default)
    {
        if (request?.UserIds == null || request.UserIds.Count == 0)
            throw new ArgumentException("Vui lòng chọn ít nhất một người dùng để thêm vào nhóm.");

        var (conv, actorMember) = await LoadGroupForActorAsync(conversationId, actorUserId, requireNotDisbanded: true, ct);

        var candidateIds = request.UserIds
            .Where(id => id != Guid.Empty)
            .Distinct()
            .ToList();

        if (candidateIds.Count == 0)
            throw new ArgumentException("Danh sách người dùng không hợp lệ.");

        var validUsers = await _context.Users
            .Where(u => candidateIds.Contains(u.Id) && u.IsActive)
            .ToListAsync(ct);

        if (validUsers.Count == 0)
            throw new KeyNotFoundException("Không tìm thấy người dùng hợp lệ để thêm vào nhóm.");

        var existingMemberIds = conv.Members.Select(m => m.UserId).ToHashSet();
        var usersToProcess = validUsers
            .Where(u => !existingMemberIds.Contains(u.Id))
            .ToList();

        var now = DateTime.UtcNow;

        if (usersToProcess.Count == 0)
        {
            return new GroupEventDto
            {
                ConversationId = conv.Id,
                EventType = "MembersAdded",
                ActorUserId = actorUserId,
                AffectedUserIds = new List<Guid>(),
                Conversation = await BuildConversationDtoAsync(conv.Id, actorUserId, ct),
                Timestamp = now
            };
        }

        var targetIds = usersToProcess.Select(u => u.Id).ToList();

        // If group requires join approval and caller is a regular Member -> create pending join requests
        if (conv.RequireJoinApproval && actorMember.Role == ConversationMemberRole.Member)
        {
            var existingPending = await _context.GroupJoinRequests
                .Where(r => r.ConversationId == conversationId
                            && targetIds.Contains(r.UserId)
                            && r.Status == JoinRequestStatus.Pending)
                .ToListAsync(ct);

            var pendingByUserId = existingPending.ToDictionary(r => r.UserId);
            GroupJoinRequest? firstRequest = null;

            foreach (var targetUser in usersToProcess)
            {
                if (pendingByUserId.TryGetValue(targetUser.Id, out var existingReq))
                {
                    firstRequest ??= existingReq;
                    continue;
                }

                var newReq = new GroupJoinRequest
                {
                    ConversationId = conversationId,
                    UserId = targetUser.Id,
                    RequestedById = actorUserId,
                    Status = JoinRequestStatus.Pending,
                    CreatedAt = now,
                    UpdatedAt = now
                };
                _context.GroupJoinRequests.Add(newReq);
                firstRequest ??= newReq;
            }

            await _context.SaveChangesAsync(ct);

            var firstUser = firstRequest != null
                ? usersToProcess.First(u => u.Id == firstRequest.UserId)
                : usersToProcess[0];

            return new GroupEventDto
            {
                ConversationId = conv.Id,
                EventType = "JoinRequestCreated",
                ActorUserId = actorUserId,
                AffectedUserIds = targetIds,
                Conversation = await BuildConversationDtoAsync(conv.Id, actorUserId, ct),
                JoinRequest = firstRequest != null ? MapJoinRequestToDto(firstRequest, firstUser, actorMember.User) : null,
                Timestamp = now
            };
        }

        // Direct add (Owner/Admin, or group doesn't require join approval)
        foreach (var targetUser in usersToProcess)
        {
            _context.ConversationMembers.Add(new ConversationMember
            {
                ConversationId = conversationId,
                UserId = targetUser.Id,
                Role = ConversationMemberRole.Member,
                JoinedAt = now
            });
        }

        // Mark any pending join requests for these users as Approved
        var pendingRequests = await _context.GroupJoinRequests
            .Where(r => r.ConversationId == conversationId
                        && targetIds.Contains(r.UserId)
                        && r.Status == JoinRequestStatus.Pending)
            .ToListAsync(ct);

        foreach (var pending in pendingRequests)
        {
            pending.Status = JoinRequestStatus.Approved;
            pending.ReviewedById = actorUserId;
            pending.ReviewedAt = now;
            pending.UpdatedAt = now;
        }

        var addedNames = string.Join(", ", usersToProcess.Select(GetDisplayLabel));
        var sysMsg = CreateSystemMessage(
            conv,
            actorUserId,
            $"{GetDisplayLabel(actorMember.User)} đã thêm {addedNames} vào nhóm",
            now);

        await _context.SaveChangesAsync(ct);

        return new GroupEventDto
        {
            ConversationId = conv.Id,
            EventType = "MembersAdded",
            ActorUserId = actorUserId,
            AffectedUserIds = targetIds,
            Conversation = await BuildConversationDtoAsync(conv.Id, actorUserId, ct),
            SystemMessage = MapSystemMessageToDto(sysMsg, actorMember.User),
            Timestamp = now
        };
    }

    public async Task<GroupEventDto> RemoveMemberAsync(
        Guid conversationId,
        Guid actorUserId,
        Guid targetUserId,
        CancellationToken ct = default)
    {
        var (conv, actorMember) = await LoadGroupForActorAsync(conversationId, actorUserId, requireNotDisbanded: true, ct);

        if (actorUserId == targetUserId)
            throw new InvalidOperationException("Vui lòng sử dụng chức năng rời nhóm để tự rời khỏi nhóm.");

        var targetMember = conv.Members.FirstOrDefault(m => m.UserId == targetUserId)
            ?? throw new KeyNotFoundException("Người dùng không phải là thành viên của nhóm.");

        if (targetMember.Role == ConversationMemberRole.Owner)
            throw new UnauthorizedAccessException("Không thể xóa trưởng nhóm khỏi nhóm.");

        if (actorMember.Role == ConversationMemberRole.Member)
            throw new UnauthorizedAccessException("Bạn không có quyền xóa thành viên khỏi nhóm.");

        if (actorMember.Role == ConversationMemberRole.Admin && targetMember.Role != ConversationMemberRole.Member)
            throw new UnauthorizedAccessException("Phó nhóm chỉ có quyền xóa thành viên thường.");

        var now = DateTime.UtcNow;
        var targetName = GetDisplayLabel(targetMember.User);

        _context.ConversationMembers.Remove(targetMember);

        var sysMsg = CreateSystemMessage(
            conv,
            actorUserId,
            $"{GetDisplayLabel(actorMember.User)} đã xóa {targetName} khỏi nhóm",
            now);

        await _context.SaveChangesAsync(ct);

        return new GroupEventDto
        {
            ConversationId = conv.Id,
            EventType = "MemberRemoved",
            ActorUserId = actorUserId,
            AffectedUserIds = new List<Guid> { targetUserId },
            Conversation = await BuildConversationDtoAsync(conv.Id, actorUserId, ct),
            SystemMessage = MapSystemMessageToDto(sysMsg, actorMember.User),
            Timestamp = now
        };
    }

    public async Task<GroupEventDto> UpdateMemberRoleAsync(
        Guid conversationId,
        Guid actorUserId,
        Guid targetUserId,
        UpdateMemberRoleRequest request,
        CancellationToken ct = default)
    {
        if (request == null || string.IsNullOrWhiteSpace(request.Role))
            throw new ArgumentException("Vui lòng chỉ định vai trò hợp lệ ('Admin' hoặc 'Member').");

        var (conv, actorMember) = await LoadGroupForActorAsync(conversationId, actorUserId, requireNotDisbanded: true, ct);

        if (actorMember.Role != ConversationMemberRole.Owner)
            throw new UnauthorizedAccessException("Chỉ trưởng nhóm mới có quyền bổ nhiệm hoặc miễn nhiệm phó nhóm.");

        if (!Enum.TryParse<ConversationMemberRole>(request.Role.Trim(), ignoreCase: true, out var newRole))
            throw new ArgumentException("Vai trò không hợp lệ. Chỉ hỗ trợ 'Admin' hoặc 'Member'.");

        if (newRole == ConversationMemberRole.Owner)
            throw new InvalidOperationException("Không thể đặt vai trò trưởng nhóm qua thao tác này. Vui lòng dùng chức năng chuyển quyền trưởng nhóm.");

        if (actorUserId == targetUserId)
            throw new InvalidOperationException("Không thể tự thay đổi vai trò của trưởng nhóm.");

        var targetMember = conv.Members.FirstOrDefault(m => m.UserId == targetUserId)
            ?? throw new KeyNotFoundException("Người dùng không phải là thành viên của nhóm.");

        var now = DateTime.UtcNow;
        MessageDto? sysMsgDto = null;

        if (targetMember.Role != newRole)
        {
            targetMember.Role = newRole;
            var content = newRole == ConversationMemberRole.Admin
                ? $"{GetDisplayLabel(actorMember.User)} đã bổ nhiệm {GetDisplayLabel(targetMember.User)} làm phó nhóm"
                : $"{GetDisplayLabel(actorMember.User)} đã gỡ quyền phó nhóm của {GetDisplayLabel(targetMember.User)}";

            var sysMsg = CreateSystemMessage(conv, actorUserId, content, now);
            await _context.SaveChangesAsync(ct);
            sysMsgDto = MapSystemMessageToDto(sysMsg, actorMember.User);
        }

        return new GroupEventDto
        {
            ConversationId = conv.Id,
            EventType = "MemberRoleUpdated",
            ActorUserId = actorUserId,
            AffectedUserIds = new List<Guid> { targetUserId },
            Conversation = await BuildConversationDtoAsync(conv.Id, actorUserId, ct),
            SystemMessage = sysMsgDto,
            Timestamp = now
        };
    }

    public async Task<GroupEventDto> UpdateNicknameAsync(
        Guid conversationId,
        Guid actorUserId,
        Guid targetUserId,
        UpdateNicknameRequest request,
        CancellationToken ct = default)
    {
        if (request == null)
            throw new ArgumentException("Dữ liệu biệt danh không hợp lệ.");

        var conv = await _context.Conversations
            .Include(c => c.Members)
                .ThenInclude(m => m.User)
            .FirstOrDefaultAsync(c => c.Id == conversationId, ct)
            ?? throw new KeyNotFoundException("Không tìm thấy cuộc trò chuyện.");

        if (conv.IsDisbanded)
            throw new InvalidOperationException("Nhóm này đã bị giải tán.");

        var actorMember = conv.Members.FirstOrDefault(m => m.UserId == actorUserId)
            ?? throw new UnauthorizedAccessException("Bạn không phải là thành viên của cuộc trò chuyện này.");

        var targetMember = conv.Members.FirstOrDefault(m => m.UserId == targetUserId)
            ?? throw new KeyNotFoundException("Thành viên không tồn tại trong cuộc trò chuyện này.");

        if (actorUserId != targetUserId
            && actorMember.Role != ConversationMemberRole.Owner
            && actorMember.Role != ConversationMemberRole.Admin)
        {
            throw new UnauthorizedAccessException("Bạn chỉ có thể đổi biệt danh của chính mình trừ khi là trưởng nhóm hoặc phó nhóm.");
        }

        var normalizedNickname = string.IsNullOrWhiteSpace(request.Nickname)
            ? null
            : request.Nickname.Trim();

        if (normalizedNickname != null && normalizedNickname.Length > 100)
            throw new ArgumentException("Biệt danh không được vượt quá 100 ký tự.");

        var now = DateTime.UtcNow;
        MessageDto? sysMsgDto = null;

        if (!string.Equals(targetMember.Nickname, normalizedNickname, StringComparison.Ordinal))
        {
            targetMember.Nickname = normalizedNickname;

            string content;
            if (normalizedNickname != null)
            {
                content = actorUserId == targetUserId
                    ? $"{GetDisplayLabel(actorMember.User)} đã đặt biệt danh thành \"{normalizedNickname}\""
                    : $"{GetDisplayLabel(actorMember.User)} đã đặt biệt danh cho {GetDisplayLabel(targetMember.User)} thành \"{normalizedNickname}\"";
            }
            else
            {
                content = actorUserId == targetUserId
                    ? $"{GetDisplayLabel(actorMember.User)} đã xóa biệt danh của mình"
                    : $"{GetDisplayLabel(actorMember.User)} đã xóa biệt danh của {GetDisplayLabel(targetMember.User)}";
            }

            var sysMsg = CreateSystemMessage(conv, actorUserId, content, now);
            await _context.SaveChangesAsync(ct);
            sysMsgDto = MapSystemMessageToDto(sysMsg, actorMember.User);
        }

        return new GroupEventDto
        {
            ConversationId = conv.Id,
            EventType = "MemberNicknameUpdated",
            ActorUserId = actorUserId,
            AffectedUserIds = new List<Guid> { targetUserId },
            Conversation = await BuildConversationDtoAsync(conv.Id, actorUserId, ct),
            SystemMessage = sysMsgDto,
            Timestamp = now
        };
    }

    public async Task<GroupEventDto> TransferOwnershipAsync(
        Guid conversationId,
        Guid currentOwnerId,
        TransferOwnershipRequest request,
        CancellationToken ct = default)
    {
        if (request == null || request.NewOwnerUserId == Guid.Empty)
            throw new ArgumentException("Vui lòng chọn thành viên nhận quyền trưởng nhóm.");

        var (conv, currentOwner) = await LoadGroupForActorAsync(conversationId, currentOwnerId, requireNotDisbanded: true, ct);

        if (currentOwner.Role != ConversationMemberRole.Owner)
            throw new UnauthorizedAccessException("Chỉ trưởng nhóm mới có quyền chuyển quyền trưởng nhóm.");

        if (request.NewOwnerUserId == currentOwnerId)
            throw new InvalidOperationException("Bạn hiện đã là trưởng nhóm.");

        var newOwner = conv.Members.FirstOrDefault(m => m.UserId == request.NewOwnerUserId)
            ?? throw new KeyNotFoundException("Người nhận quyền không phải là thành viên của nhóm.");

        if (!newOwner.User.IsActive)
            throw new InvalidOperationException("Tài khoản người nhận quyền không còn hoạt động.");

        var now = DateTime.UtcNow;
        currentOwner.Role = ConversationMemberRole.Admin;
        newOwner.Role = ConversationMemberRole.Owner;

        var sysMsg = CreateSystemMessage(
            conv,
            currentOwnerId,
            $"{GetDisplayLabel(currentOwner.User)} đã chuyển quyền trưởng nhóm cho {GetDisplayLabel(newOwner.User)}",
            now);

        await _context.SaveChangesAsync(ct);

        return new GroupEventDto
        {
            ConversationId = conv.Id,
            EventType = "OwnershipTransferred",
            ActorUserId = currentOwnerId,
            AffectedUserIds = new List<Guid> { currentOwnerId, request.NewOwnerUserId },
            Conversation = await BuildConversationDtoAsync(conv.Id, currentOwnerId, ct),
            SystemMessage = MapSystemMessageToDto(sysMsg, currentOwner.User),
            Timestamp = now
        };
    }

    public async Task<GroupEventDto> LeaveGroupAsync(
        Guid conversationId,
        Guid userId,
        LeaveGroupRequest? request = null,
        CancellationToken ct = default)
    {
        var (conv, actorMember) = await LoadGroupForActorAsync(conversationId, userId, requireNotDisbanded: false, ct);
        var now = DateTime.UtcNow;
        var otherMembers = conv.Members.Where(m => m.UserId != userId).ToList();

        // Last member leaves -> auto-disband group
        if (otherMembers.Count == 0)
        {
            _context.ConversationMembers.Remove(actorMember);
            conv.IsDisbanded = true;
            conv.DisbandedAt ??= now;
            conv.IsInviteLinkEnabled = false;
            conv.UpdatedAt = now;

            await RejectPendingJoinRequestsAsync(conversationId, userId, now, ct);
            await _context.SaveChangesAsync(ct);

            return new GroupEventDto
            {
                ConversationId = conv.Id,
                EventType = "GroupDisbanded",
                ActorUserId = userId,
                AffectedUserIds = new List<Guid> { userId },
                Conversation = await BuildConversationDtoAsync(conv.Id, null, ct),
                SystemMessage = null,
                Timestamp = now
            };
        }

        string sysContent;
        if (actorMember.Role == ConversationMemberRole.Owner && !conv.IsDisbanded)
        {
            if (request?.NewOwnerUserId == null || request.NewOwnerUserId.Value == Guid.Empty)
            {
                throw new InvalidOperationException("Trưởng nhóm cần chuyển quyền trước khi rời nhóm.");
            }

            var newOwner = otherMembers.FirstOrDefault(m => m.UserId == request.NewOwnerUserId.Value)
                ?? throw new InvalidOperationException("Người nhận quyền trưởng nhóm không phải là thành viên hợp lệ trong nhóm.");

            if (!newOwner.User.IsActive)
                throw new InvalidOperationException("Tài khoản người nhận quyền không còn hoạt động.");

            newOwner.Role = ConversationMemberRole.Owner;
            sysContent = $"{GetDisplayLabel(actorMember.User)} đã chuyển quyền trưởng nhóm cho {GetDisplayLabel(newOwner.User)} và rời khỏi nhóm";
        }
        else
        {
            sysContent = $"{GetDisplayLabel(actorMember.User)} đã rời khỏi nhóm";
        }

        _context.ConversationMembers.Remove(actorMember);
        var sysMsg = CreateSystemMessage(conv, userId, sysContent, now);
        await _context.SaveChangesAsync(ct);

        return new GroupEventDto
        {
            ConversationId = conv.Id,
            EventType = "MemberLeft",
            ActorUserId = userId,
            AffectedUserIds = new List<Guid> { userId },
            Conversation = await BuildConversationDtoAsync(conv.Id, null, ct),
            SystemMessage = MapSystemMessageToDto(sysMsg, actorMember.User),
            Timestamp = now
        };
    }

    public async Task<GroupEventDto> DisbandGroupAsync(
        Guid conversationId,
        Guid ownerUserId,
        CancellationToken ct = default)
    {
        var (conv, ownerMember) = await LoadGroupForActorAsync(conversationId, ownerUserId, requireNotDisbanded: true, ct);

        if (ownerMember.Role != ConversationMemberRole.Owner)
            throw new UnauthorizedAccessException("Chỉ trưởng nhóm mới có quyền giải tán nhóm.");

        var now = DateTime.UtcNow;
        var memberIds = conv.Members.Select(m => m.UserId).ToList();

        conv.IsDisbanded = true;
        conv.DisbandedAt = now;
        conv.IsInviteLinkEnabled = false;

        await RejectPendingJoinRequestsAsync(conversationId, ownerUserId, now, ct);

        var sysMsg = CreateSystemMessage(
            conv,
            ownerUserId,
            $"{GetDisplayLabel(ownerMember.User)} đã giải tán nhóm",
            now);

        await _context.SaveChangesAsync(ct);

        return new GroupEventDto
        {
            ConversationId = conv.Id,
            EventType = "GroupDisbanded",
            ActorUserId = ownerUserId,
            AffectedUserIds = memberIds,
            Conversation = await BuildConversationDtoAsync(conv.Id, ownerUserId, ct),
            SystemMessage = MapSystemMessageToDto(sysMsg, ownerMember.User),
            Timestamp = now
        };
    }

    public async Task<DateTime?> MuteConversationAsync(
        Guid conversationId,
        Guid userId,
        MuteConversationRequest request,
        CancellationToken ct = default)
    {
        var member = await _context.ConversationMembers
            .FirstOrDefaultAsync(cm => cm.ConversationId == conversationId && cm.UserId == userId, ct);

        if (member == null)
        {
            var exists = await _context.Conversations
                .AsNoTracking()
                .AnyAsync(c => c.Id == conversationId, ct);

            if (!exists)
                throw new KeyNotFoundException("Không tìm thấy cuộc trò chuyện.");

            throw new UnauthorizedAccessException("Bạn không phải là thành viên của cuộc trò chuyện này.");
        }

        DateTime? mutedUntil = null;
        if (request != null)
        {
            if (request.MutedUntil.HasValue)
            {
                mutedUntil = request.MutedUntil.Value.Kind == DateTimeKind.Unspecified
                    ? DateTime.SpecifyKind(request.MutedUntil.Value, DateTimeKind.Utc)
                    : request.MutedUntil.Value.ToUniversalTime();
            }
            else if (request.DurationMinutes.HasValue)
            {
                mutedUntil = request.DurationMinutes.Value > 0
                    ? DateTime.UtcNow.AddMinutes(request.DurationMinutes.Value)
                    : null;
            }
        }

        member.MutedUntil = mutedUntil;
        await _context.SaveChangesAsync(ct);

        return member.MutedUntil;
    }

    public async Task<string> RegenerateInviteCodeAsync(
        Guid conversationId,
        Guid actorUserId,
        CancellationToken ct = default)
    {
        var (conv, actorMember) = await LoadGroupForActorAsync(conversationId, actorUserId, requireNotDisbanded: true, ct);
        EnsureOwnerOrAdmin(actorMember, "Chỉ trưởng nhóm hoặc phó nhóm mới có quyền đặt lại liên kết mời.");

        conv.InviteCode = await GenerateUniqueInviteCodeAsync(ct);
        conv.IsInviteLinkEnabled = true;
        conv.UpdatedAt = DateTime.UtcNow;

        await _context.SaveChangesAsync(ct);

        return conv.InviteCode;
    }

    public async Task<GroupInvitePreviewDto> GetInvitePreviewAsync(
        string inviteCode,
        Guid userId,
        CancellationToken ct = default)
    {
        var trimmedCode = inviteCode?.Trim() ?? string.Empty;
        if (string.IsNullOrWhiteSpace(trimmedCode))
            throw new ArgumentException("Mã mời nhóm không hợp lệ.");

        var conv = await _context.Conversations
            .AsNoTracking()
            .Include(c => c.Members)
            .FirstOrDefaultAsync(c => c.Type == ConversationType.Group
                                      && c.InviteCode == trimmedCode
                                      && !c.IsDisbanded
                                      && c.IsInviteLinkEnabled, ct)
            ?? throw new KeyNotFoundException("Liên kết mời không tồn tại hoặc đã bị vô hiệu hóa.");

        var isAlreadyMember = conv.Members.Any(m => m.UserId == userId);
        var hasPendingRequest = !isAlreadyMember && await _context.GroupJoinRequests
            .AsNoTracking()
            .AnyAsync(r => r.ConversationId == conv.Id
                           && r.UserId == userId
                           && r.Status == JoinRequestStatus.Pending, ct);

        return new GroupInvitePreviewDto
        {
            ConversationId = conv.Id,
            Name = conv.Name ?? string.Empty,
            AvatarUrl = conv.AvatarUrl,
            MemberCount = conv.Members.Count,
            RequireJoinApproval = conv.RequireJoinApproval,
            IsAlreadyMember = isAlreadyMember,
            HasPendingRequest = hasPendingRequest,
            InviteCode = conv.InviteCode ?? trimmedCode
        };
    }

    public async Task<GroupEventDto> JoinByInviteCodeAsync(
        string inviteCode,
        Guid userId,
        CancellationToken ct = default)
    {
        var trimmedCode = inviteCode?.Trim() ?? string.Empty;
        if (string.IsNullOrWhiteSpace(trimmedCode))
            throw new ArgumentException("Mã mời nhóm không hợp lệ.");

        var user = await _context.Users.FirstOrDefaultAsync(u => u.Id == userId, ct)
            ?? throw new KeyNotFoundException("Không tìm thấy người dùng.");

        if (!user.IsActive)
            throw new InvalidOperationException("Tài khoản của bạn không còn hoạt động.");

        var conv = await _context.Conversations
            .Include(c => c.Members)
                .ThenInclude(m => m.User)
            .FirstOrDefaultAsync(c => c.Type == ConversationType.Group
                                      && c.InviteCode == trimmedCode
                                      && !c.IsDisbanded
                                      && c.IsInviteLinkEnabled, ct)
            ?? throw new KeyNotFoundException("Liên kết mời không tồn tại hoặc đã bị vô hiệu hóa.");

        var now = DateTime.UtcNow;

        if (conv.Members.Any(m => m.UserId == userId))
        {
            return new GroupEventDto
            {
                ConversationId = conv.Id,
                EventType = "AlreadyMember",
                ActorUserId = userId,
                AffectedUserIds = new List<Guid> { userId },
                Conversation = await BuildConversationDtoAsync(conv.Id, userId, ct),
                Timestamp = now
            };
        }

        if (conv.RequireJoinApproval)
        {
            var existingPending = await _context.GroupJoinRequests
                .FirstOrDefaultAsync(r => r.ConversationId == conv.Id
                                          && r.UserId == userId
                                          && r.Status == JoinRequestStatus.Pending, ct);

            if (existingPending == null)
            {
                existingPending = new GroupJoinRequest
                {
                    ConversationId = conv.Id,
                    UserId = userId,
                    RequestedById = userId,
                    Status = JoinRequestStatus.Pending,
                    CreatedAt = now,
                    UpdatedAt = now
                };
                _context.GroupJoinRequests.Add(existingPending);
                await _context.SaveChangesAsync(ct);
            }

            return new GroupEventDto
            {
                ConversationId = conv.Id,
                EventType = "JoinRequestCreated",
                ActorUserId = userId,
                AffectedUserIds = new List<Guid> { userId },
                JoinRequest = MapJoinRequestToDto(existingPending, user, user),
                Timestamp = now
            };
        }

        _context.ConversationMembers.Add(new ConversationMember
        {
            ConversationId = conv.Id,
            UserId = userId,
            Role = ConversationMemberRole.Member,
            JoinedAt = now
        });

        var pendingRequests = await _context.GroupJoinRequests
            .Where(r => r.ConversationId == conv.Id
                        && r.UserId == userId
                        && r.Status == JoinRequestStatus.Pending)
            .ToListAsync(ct);

        foreach (var pending in pendingRequests)
        {
            pending.Status = JoinRequestStatus.Approved;
            pending.ReviewedById = userId;
            pending.ReviewedAt = now;
            pending.UpdatedAt = now;
        }

        var sysMsg = CreateSystemMessage(
            conv,
            userId,
            $"{GetDisplayLabel(user)} đã tham gia nhóm qua liên kết mời",
            now);

        await _context.SaveChangesAsync(ct);

        return new GroupEventDto
        {
            ConversationId = conv.Id,
            EventType = "MembersAdded",
            ActorUserId = userId,
            AffectedUserIds = new List<Guid> { userId },
            Conversation = await BuildConversationDtoAsync(conv.Id, userId, ct),
            SystemMessage = MapSystemMessageToDto(sysMsg, user),
            Timestamp = now
        };
    }

    public async Task<IReadOnlyList<GroupJoinRequestDto>> GetJoinRequestsAsync(
        Guid conversationId,
        Guid actorUserId,
        CancellationToken ct = default)
    {
        var (_, actorMember) = await LoadGroupForActorAsync(conversationId, actorUserId, requireNotDisbanded: false, ct);
        EnsureOwnerOrAdmin(actorMember, "Chỉ trưởng nhóm hoặc phó nhóm mới có quyền xem danh sách yêu cầu tham gia.");

        var requests = await _context.GroupJoinRequests
            .AsNoTracking()
            .Include(r => r.User)
            .Include(r => r.RequestedBy)
            .Where(r => r.ConversationId == conversationId && r.Status == JoinRequestStatus.Pending)
            .OrderByDescending(r => r.CreatedAt)
            .ToListAsync(ct);

        return requests.Select(r => MapJoinRequestToDto(r, r.User, r.RequestedBy)).ToList();
    }

    public async Task<GroupEventDto> ReviewJoinRequestAsync(
        Guid conversationId,
        Guid requestId,
        Guid actorUserId,
        ReviewJoinRequestDto request,
        CancellationToken ct = default)
    {
        if (request == null)
            throw new ArgumentException("Dữ liệu duyệt yêu cầu không hợp lệ.");

        var (conv, actorMember) = await LoadGroupForActorAsync(conversationId, actorUserId, requireNotDisbanded: true, ct);
        EnsureOwnerOrAdmin(actorMember, "Chỉ trưởng nhóm hoặc phó nhóm mới có quyền duyệt yêu cầu tham gia.");

        var joinRequest = await _context.GroupJoinRequests
            .Include(r => r.User)
            .Include(r => r.RequestedBy)
            .FirstOrDefaultAsync(r => r.Id == requestId && r.ConversationId == conversationId, ct)
            ?? throw new KeyNotFoundException("Không tìm thấy yêu cầu tham gia nhóm.");

        if (joinRequest.Status != JoinRequestStatus.Pending)
            throw new InvalidOperationException("Yêu cầu tham gia này đã được xử lý trước đó.");

        var now = DateTime.UtcNow;
        joinRequest.ReviewedById = actorUserId;
        joinRequest.ReviewedAt = now;
        joinRequest.UpdatedAt = now;

        MessageDto? sysMsgDto = null;
        string eventType;

        if (request.Approve)
        {
            if (!joinRequest.User.IsActive)
                throw new InvalidOperationException("Tài khoản người yêu cầu không còn hoạt động.");

            joinRequest.Status = JoinRequestStatus.Approved;
            eventType = "JoinRequestApproved";

            var alreadyMember = conv.Members.Any(m => m.UserId == joinRequest.UserId);
            if (!alreadyMember)
            {
                _context.ConversationMembers.Add(new ConversationMember
                {
                    ConversationId = conversationId,
                    UserId = joinRequest.UserId,
                    Role = ConversationMemberRole.Member,
                    JoinedAt = now
                });

                var sysMsg = CreateSystemMessage(
                    conv,
                    actorUserId,
                    $"{GetDisplayLabel(actorMember.User)} đã duyệt {GetDisplayLabel(joinRequest.User)} tham gia nhóm",
                    now);

                await _context.SaveChangesAsync(ct);
                sysMsgDto = MapSystemMessageToDto(sysMsg, actorMember.User);
            }
            else
            {
                await _context.SaveChangesAsync(ct);
            }
        }
        else
        {
            joinRequest.Status = JoinRequestStatus.Rejected;
            eventType = "JoinRequestRejected";
            await _context.SaveChangesAsync(ct);
        }

        return new GroupEventDto
        {
            ConversationId = conv.Id,
            EventType = eventType,
            ActorUserId = actorUserId,
            AffectedUserIds = new List<Guid> { joinRequest.UserId },
            Conversation = await BuildConversationDtoAsync(conv.Id, actorUserId, ct),
            JoinRequest = MapJoinRequestToDto(joinRequest, joinRequest.User, joinRequest.RequestedBy),
            SystemMessage = sysMsgDto,
            Timestamp = now
        };
    }

    // =====================================================
    // Private helpers
    // =====================================================

    private async Task<(Conversation Conversation, ConversationMember ActorMember)> LoadGroupForActorAsync(
        Guid conversationId,
        Guid actorUserId,
        bool requireNotDisbanded,
        CancellationToken ct)
    {
        var conv = await _context.Conversations
            .Include(c => c.Members)
                .ThenInclude(m => m.User)
            .FirstOrDefaultAsync(c => c.Id == conversationId, ct)
            ?? throw new KeyNotFoundException("Không tìm thấy cuộc trò chuyện.");

        if (conv.Type != ConversationType.Group)
            throw new InvalidOperationException("Thao tác này chỉ áp dụng cho nhóm chat.");

        var actorMember = conv.Members.FirstOrDefault(m => m.UserId == actorUserId)
            ?? throw new UnauthorizedAccessException("Bạn không phải là thành viên của nhóm này.");

        if (requireNotDisbanded && conv.IsDisbanded)
            throw new InvalidOperationException("Nhóm này đã bị giải tán.");

        return (conv, actorMember);
    }

    private static void EnsureOwnerOrAdmin(ConversationMember member, string errorMessage)
    {
        if (member.Role != ConversationMemberRole.Owner && member.Role != ConversationMemberRole.Admin)
        {
            throw new UnauthorizedAccessException(errorMessage);
        }
    }

    private Message CreateSystemMessage(Conversation conversation, Guid actorUserId, string content, DateTime now)
    {
        var message = new Message
        {
            ConversationId = conversation.Id,
            SenderId = actorUserId,
            Type = MessageType.System,
            Content = content,
            CreatedAt = now,
            UpdatedAt = now
        };

        _context.Messages.Add(message);
        conversation.UpdatedAt = now;
        return message;
    }

    private async Task RejectPendingJoinRequestsAsync(Guid conversationId, Guid reviewerId, DateTime now, CancellationToken ct)
    {
        var pending = await _context.GroupJoinRequests
            .Where(r => r.ConversationId == conversationId && r.Status == JoinRequestStatus.Pending)
            .ToListAsync(ct);

        foreach (var req in pending)
        {
            req.Status = JoinRequestStatus.Rejected;
            req.ReviewedById = reviewerId;
            req.ReviewedAt = now;
            req.UpdatedAt = now;
        }
    }

    private async Task<string> GenerateUniqueInviteCodeAsync(CancellationToken ct)
    {
        for (var attempt = 0; attempt < 10; attempt++)
        {
            var chars = new char[InviteCodeLength];
            for (var i = 0; i < InviteCodeLength; i++)
            {
                chars[i] = InviteCodeAlphabet[RandomNumberGenerator.GetInt32(InviteCodeAlphabet.Length)];
            }

            var code = new string(chars);
            var exists = await _context.Conversations
                .AsNoTracking()
                .AnyAsync(c => c.InviteCode == code, ct);

            if (!exists)
                return code;
        }

        return Guid.NewGuid().ToString("N")[..16];
    }

    private async Task<ConversationDto> BuildConversationDtoAsync(
        Guid conversationId,
        Guid? viewerUserId,
        CancellationToken ct)
    {
        var conv = await _context.Conversations
            .AsNoTracking()
            .Include(c => c.Members)
                .ThenInclude(m => m.User)
            .FirstAsync(c => c.Id == conversationId, ct);

        var lastMsg = await _context.Messages
            .AsNoTracking()
            .Include(m => m.Sender)
            .Include(m => m.Attachments)
            .Where(m => m.ConversationId == conversationId)
            .OrderByDescending(m => m.CreatedAt)
            .FirstOrDefaultAsync(ct);

        var pinnedCount = await _context.Messages
            .AsNoTracking()
            .CountAsync(m => m.ConversationId == conversationId && m.IsPinned && !m.IsDeleted, ct);

        var memberIds = conv.Members.Select(m => m.UserId).ToList();
        var onlineSet = _presenceService != null && memberIds.Count > 0
            ? (await _presenceService.GetOnlineUsersAsync(memberIds)).ToHashSet()
            : new HashSet<Guid>();

        var viewerMember = viewerUserId.HasValue
            ? conv.Members.FirstOrDefault(m => m.UserId == viewerUserId.Value)
            : null;

        var unreadCount = 0;
        if (viewerMember != null && viewerUserId.HasValue)
        {
            var unreadQuery = _context.Messages
                .AsNoTracking()
                .Where(m => m.ConversationId == conversationId
                            && m.SenderId != viewerUserId.Value
                            && !m.IsDeleted);

            if (viewerMember.LastReadAt.HasValue)
            {
                unreadQuery = unreadQuery.Where(m => m.CreatedAt > viewerMember.LastReadAt.Value);
            }

            unreadCount = await unreadQuery.CountAsync(ct);
        }

        var orderedMembers = conv.Members
            .OrderByDescending(m => m.Role)
            .ThenBy(m => m.JoinedAt)
            .Select(m => new ConversationMemberDto
            {
                UserId = m.UserId,
                Username = m.User.Username,
                DisplayName = m.User.DisplayName,
                AvatarUrl = m.User.AvatarUrl,
                Role = m.Role.ToString(),
                Nickname = m.Nickname,
                MutedUntil = m.MutedUntil,
                IsOnline = onlineSet.Contains(m.UserId),
                LastSeenAt = m.User.LastSeenAt,
                LastReadAt = m.LastReadAt
            })
            .ToList();

        return new ConversationDto
        {
            Id = conv.Id,
            Type = conv.Type.ToString(),
            Name = conv.Name,
            AvatarUrl = conv.AvatarUrl,
            CreatedById = conv.CreatedById,
            InviteCode = conv.Type == ConversationType.Group && conv.IsInviteLinkEnabled && !conv.IsDisbanded
                ? conv.InviteCode
                : null,
            IsInviteLinkEnabled = conv.IsInviteLinkEnabled,
            RequireJoinApproval = conv.RequireJoinApproval,
            OnlyAdminsCanSend = conv.OnlyAdminsCanSend,
            IsDisbanded = conv.IsDisbanded,
            PinnedCount = pinnedCount,
            MutedUntil = viewerMember?.MutedUntil,
            CurrentUserRole = viewerMember?.Role.ToString(),
            Members = orderedMembers,
            LastMessage = lastMsg != null ? MapMessageSummaryToDto(lastMsg) : null,
            UnreadCount = unreadCount,
            CreatedAt = conv.CreatedAt,
            UpdatedAt = conv.UpdatedAt
        };
    }

    private static MessageDto MapSystemMessageToDto(Message msg, User actor)
    {
        return new MessageDto
        {
            Id = msg.Id,
            ConversationId = msg.ConversationId,
            SenderId = msg.SenderId,
            SenderUsername = actor.Username,
            SenderDisplayName = actor.DisplayName,
            Type = MessageType.System.ToString(),
            Content = msg.Content,
            IsDeleted = false,
            CreatedAt = msg.CreatedAt
        };
    }

    private static MessageDto MapMessageSummaryToDto(Message msg)
    {
        return new MessageDto
        {
            Id = msg.Id,
            ConversationId = msg.ConversationId,
            SenderId = msg.SenderId,
            SenderUsername = msg.Sender?.Username ?? string.Empty,
            SenderDisplayName = msg.Sender?.DisplayName ?? string.Empty,
            Type = msg.Type.ToString(),
            Content = msg.IsDeleted ? "[Tin nhắn đã bị thu hồi]" : msg.Content,
            ClientMessageId = msg.ClientMessageId,
            IsDeleted = msg.IsDeleted,
            CreatedAt = msg.CreatedAt,
            EditedAt = msg.EditedAt,
            ReplyToMessageId = msg.ReplyToMessageId,
            MentionedUserIds = ParseMentionedUserIds(msg.MentionedUserIdsJson),
            MentionAll = msg.MentionAll,
            IsPinned = msg.IsPinned,
            PinnedById = msg.PinnedById,
            PinnedAt = msg.PinnedAt,
            Attachments = msg.IsDeleted
                ? new List<MessageAttachmentDto>()
                : msg.Attachments
                    .OrderBy(a => a.SortOrder)
                    .Select(a => new MessageAttachmentDto
                    {
                        Id = a.Id,
                        AttachmentType = a.AttachmentType.ToString(),
                        StorageUrl = a.StorageUrl,
                        ThumbnailUrl = a.ThumbnailUrl,
                        MimeType = a.MimeType,
                        OriginalFileName = a.OriginalFileName,
                        FileSizeBytes = a.FileSizeBytes,
                        DurationSeconds = a.DurationSeconds,
                        WaveformJson = a.WaveformJson,
                        Width = a.Width,
                        Height = a.Height,
                        SortOrder = a.SortOrder
                    })
                    .ToList()
        };
    }

    private static GroupJoinRequestDto MapJoinRequestToDto(GroupJoinRequest req, User user, User? requestedBy)
    {
        return new GroupJoinRequestDto
        {
            Id = req.Id,
            ConversationId = req.ConversationId,
            UserId = req.UserId,
            Username = user.Username,
            DisplayName = user.DisplayName,
            AvatarUrl = user.AvatarUrl,
            RequestedById = req.RequestedById,
            RequestedByDisplayName = requestedBy != null ? GetDisplayLabel(requestedBy) : null,
            Status = req.Status.ToString(),
            ReviewedById = req.ReviewedById,
            ReviewedAt = req.ReviewedAt,
            CreatedAt = req.CreatedAt
        };
    }

    private static List<Guid> ParseMentionedUserIds(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
            return new List<Guid>();

        try
        {
            return JsonSerializer.Deserialize<List<Guid>>(json) ?? new List<Guid>();
        }
        catch
        {
            return new List<Guid>();
        }
    }

    private static string GetDisplayLabel(User user)
    {
        return !string.IsNullOrWhiteSpace(user.DisplayName) ? user.DisplayName : user.Username;
    }
}
