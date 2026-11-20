using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using MySocialApp.Application.DTOs.Chat;
using MySocialApp.Application.DTOs.Common;
using MySocialApp.Application.Interfaces;
using MySocialApp.Domain.Entities;
using MySocialApp.Domain.Enums;

namespace MySocialApp.Application.Services;

public class ChatService : IChatService
{
    private const string DeletedMessagePlaceholder = "[Tin nhắn đã bị thu hồi]";

    private readonly IApplicationDbContext _context;
    private readonly IUserPresenceService? _presenceService;
    private readonly IMediaStorageService? _mediaStorageService;

    public ChatService(
        IApplicationDbContext context,
        IUserPresenceService? presenceService = null,
        IMediaStorageService? mediaStorageService = null)
    {
        _context = context;
        _presenceService = presenceService;
        _mediaStorageService = mediaStorageService;
    }

    public async Task<PagedResult<ConversationDto>> GetConversationsAsync(Guid userId, PaginationParams pagination)
    {
        var query = _context.Conversations
            .Include(c => c.Members).ThenInclude(m => m.User)
            .Include(c => c.Messages.OrderByDescending(m => m.CreatedAt).Take(1))
                .ThenInclude(m => m.Sender)
            .Include(c => c.Messages.OrderByDescending(m => m.CreatedAt).Take(1))
                .ThenInclude(m => m.Attachments)
            .Where(c => c.Members.Any(m => m.UserId == userId))
            .OrderByDescending(c => c.UpdatedAt);

        var totalItems = await query.CountAsync();
        var conversations = await query
            .Skip((pagination.Page - 1) * pagination.PageSize)
            .Take(pagination.PageSize)
            .ToListAsync();

        var convIds = conversations.Select(c => c.Id).ToList();

        // Calculate unread count for each conversation for this user
        var memberRecords = await _context.ConversationMembers
            .Where(cm => convIds.Contains(cm.ConversationId) && cm.UserId == userId)
            .ToListAsync();

        var unreadCounts = new Dictionary<Guid, int>();
        foreach (var member in memberRecords)
        {
            var unreadQuery = _context.Messages
                .Where(m => m.ConversationId == member.ConversationId
                            && m.SenderId != userId
                            && !m.IsDeleted);

            if (member.LastReadAt.HasValue)
            {
                unreadQuery = unreadQuery.Where(m => m.CreatedAt > member.LastReadAt.Value);
            }

            var count = await unreadQuery.CountAsync();
            unreadCounts[member.ConversationId] = count;
        }

        // Batch pinned counts per conversation
        var pinnedCounts = convIds.Count > 0
            ? await _context.Messages
                .Where(m => convIds.Contains(m.ConversationId) && m.IsPinned && !m.IsDeleted)
                .GroupBy(m => m.ConversationId)
                .Select(g => new { ConversationId = g.Key, Count = g.Count() })
                .ToDictionaryAsync(x => x.ConversationId, x => x.Count)
            : new Dictionary<Guid, int>();

        // Batch online presence lookup
        var allMemberIds = conversations
            .SelectMany(c => c.Members.Select(m => m.UserId))
            .Distinct()
            .ToList();

        var onlineUserIds = await GetOnlineUserSetAsync(allMemberIds);

        var dtos = conversations.Select(c => MapConversationToDto(
            c,
            userId,
            onlineUserIds,
            unreadCounts.GetValueOrDefault(c.Id, 0),
            pinnedCounts.GetValueOrDefault(c.Id, 0))).ToList();

        return new PagedResult<ConversationDto>
        {
            Items = dtos,
            Page = pagination.Page,
            PageSize = pagination.PageSize,
            TotalItems = totalItems
        };
    }

    public async Task<ConversationDto> GetOrCreateConversationAsync(Guid userId, Guid participantId)
    {
        if (userId == participantId)
            throw new InvalidOperationException("Cannot create conversation with yourself.");

        // Check if participant exists
        var participant = await _context.Users.FindAsync(participantId)
            ?? throw new KeyNotFoundException("User not found.");

        if (!participant.IsActive)
            throw new InvalidOperationException("User account is inactive.");

        // Check existing Direct conversation between these two users (never collide with 2-person groups)
        var existing = await _context.Conversations
            .Include(c => c.Members).ThenInclude(m => m.User)
            .Include(c => c.Messages.OrderByDescending(m => m.CreatedAt).Take(1))
                .ThenInclude(m => m.Sender)
            .Include(c => c.Messages.OrderByDescending(m => m.CreatedAt).Take(1))
                .ThenInclude(m => m.Attachments)
            .Where(c => c.Type == ConversationType.Direct
                && !c.IsDisbanded
                && c.Members.Count == 2
                && c.Members.Any(m => m.UserId == userId)
                && c.Members.Any(m => m.UserId == participantId))
            .FirstOrDefaultAsync();

        if (existing != null)
        {
            var onlineSet = await GetOnlineUserSetAsync(existing.Members.Select(m => m.UserId));
            var pinnedCount = await _context.Messages
                .CountAsync(m => m.ConversationId == existing.Id && m.IsPinned && !m.IsDeleted);

            var currentMember = existing.Members.FirstOrDefault(m => m.UserId == userId);
            var unreadQuery = _context.Messages
                .Where(m => m.ConversationId == existing.Id && m.SenderId != userId && !m.IsDeleted);
            if (currentMember?.LastReadAt.HasValue == true)
            {
                unreadQuery = unreadQuery.Where(m => m.CreatedAt > currentMember.LastReadAt.Value);
            }
            var unreadCount = await unreadQuery.CountAsync();

            return MapConversationToDto(existing, userId, onlineSet, unreadCount, pinnedCount);
        }

        var conversation = new Conversation
        {
            Type = ConversationType.Direct,
            IsInviteLinkEnabled = false
        };
        conversation.Members.Add(new ConversationMember { UserId = userId, Role = ConversationMemberRole.Member });
        conversation.Members.Add(new ConversationMember { UserId = participantId, Role = ConversationMemberRole.Member });

        _context.Conversations.Add(conversation);
        await _context.SaveChangesAsync();

        var saved = await _context.Conversations
            .Include(c => c.Members).ThenInclude(m => m.User)
            .FirstAsync(c => c.Id == conversation.Id);

        var newOnlineSet = await GetOnlineUserSetAsync(saved.Members.Select(m => m.UserId));
        return MapConversationToDto(saved, userId, newOnlineSet, unreadCount: 0, pinnedCount: 0);
    }

    public async Task<PagedResult<MessageDto>> GetMessagesAsync(Guid conversationId, Guid userId, PaginationParams pagination)
    {
        var isMember = await _context.ConversationMembers
            .AnyAsync(cm => cm.ConversationId == conversationId && cm.UserId == userId);

        if (!isMember)
            throw new UnauthorizedAccessException("Not a member of this conversation.");

        var query = IncludeFullMessageDetails(_context.Messages)
            .Where(m => m.ConversationId == conversationId)
            .OrderByDescending(m => m.CreatedAt);

        var totalItems = await query.CountAsync();
        var messages = await query
            .Skip((pagination.Page - 1) * pagination.PageSize)
            .Take(pagination.PageSize)
            .ToListAsync();

        return new PagedResult<MessageDto>
        {
            Items = messages.Select(m => MapMessageToDto(m, userId)).ToList(),
            Page = pagination.Page,
            PageSize = pagination.PageSize,
            TotalItems = totalItems
        };
    }

    public async Task<MessageDto> SendMessageAsync(Guid conversationId, Guid senderId, SendMessageRequest request)
    {
        var member = await _context.ConversationMembers
            .Include(cm => cm.Conversation)
            .FirstOrDefaultAsync(cm => cm.ConversationId == conversationId && cm.UserId == senderId);

        if (member == null)
            throw new UnauthorizedAccessException("Not a member of this conversation.");

        var conversation = member.Conversation;
        if (conversation.IsDisbanded)
            throw new InvalidOperationException("Cannot send messages to a disbanded group.");

        if (conversation.Type == ConversationType.Group
            && conversation.OnlyAdminsCanSend
            && member.Role is not (ConversationMemberRole.Owner or ConversationMemberRole.Admin))
        {
            throw new UnauthorizedAccessException("Only group owners and admins can send messages in this group.");
        }

        if (string.IsNullOrWhiteSpace(request.Content))
            throw new ArgumentException("Message content cannot be empty.");

        if (request.Content.Length > 4000)
            throw new ArgumentException("Message content cannot exceed 4000 characters.");

        // Idempotency check using ClientMessageId
        if (!string.IsNullOrWhiteSpace(request.ClientMessageId))
        {
            var existingMessage = await IncludeFullMessageDetails(_context.Messages)
                .FirstOrDefaultAsync(m => m.ConversationId == conversationId
                                          && m.SenderId == senderId
                                          && m.ClientMessageId == request.ClientMessageId);

            if (existingMessage != null)
            {
                return MapMessageToDto(existingMessage, senderId);
            }
        }

        if (request.ReplyToMessageId.HasValue)
        {
            var replyExists = await _context.Messages
                .AnyAsync(m => m.Id == request.ReplyToMessageId.Value && m.ConversationId == conversationId);

            if (!replyExists)
                throw new KeyNotFoundException("Reply target message not found in this conversation.");
        }

        var mentionAll = conversation.Type == ConversationType.Group && request.MentionAll;
        var mentionedUserIdsJson = SerializeMentionedUserIds(request.MentionedUserIds);

        var message = new Message
        {
            ConversationId = conversationId,
            SenderId = senderId,
            Type = MessageType.Text,
            Content = request.Content,
            ClientMessageId = request.ClientMessageId,
            ReplyToMessageId = request.ReplyToMessageId,
            MentionedUserIdsJson = mentionedUserIdsJson,
            MentionAll = mentionAll
        };

        _context.Messages.Add(message);
        conversation.UpdatedAt = DateTime.UtcNow;

        await _context.SaveChangesAsync();

        var saved = await IncludeFullMessageDetails(_context.Messages)
            .FirstAsync(m => m.Id == message.Id);

        return MapMessageToDto(saved, senderId);
    }

    public async Task<MessageDto> EditMessageAsync(Guid messageId, Guid userId, EditMessageRequest request)
    {
        var message = await IncludeFullMessageDetails(_context.Messages)
            .Include(m => m.Conversation)
            .FirstOrDefaultAsync(m => m.Id == messageId)
            ?? throw new KeyNotFoundException("Message not found.");

        if (message.SenderId != userId)
            throw new UnauthorizedAccessException("You can only edit your own messages.");

        if (message.Conversation?.IsDisbanded == true)
            throw new InvalidOperationException("Cannot edit messages in a disbanded group.");

        if (message.IsDeleted)
            throw new InvalidOperationException("Cannot edit a deleted message.");

        if (message.Type is MessageType.System or MessageType.Poll or MessageType.Voice)
            throw new InvalidOperationException("This message type cannot be edited.");

        if (string.IsNullOrWhiteSpace(request.Content))
            throw new ArgumentException("Message content cannot be empty.");

        if (request.Content.Length > 4000)
            throw new ArgumentException("Message content cannot exceed 4000 characters.");

        message.Content = request.Content;
        message.EditedAt = DateTime.UtcNow;

        await _context.SaveChangesAsync();

        return MapMessageToDto(message, userId);
    }

    public async Task<MessageDeletedDto> DeleteMessageAsync(Guid messageId, Guid userId)
    {
        var message = await _context.Messages
            .Include(m => m.Conversation)
            .Include(m => m.Attachments)
            .FirstOrDefaultAsync(m => m.Id == messageId)
            ?? throw new KeyNotFoundException("Message not found.");

        var canDelete = message.SenderId == userId;
        if (!canDelete && message.Conversation?.Type == ConversationType.Group)
        {
            var actorRole = await _context.ConversationMembers
                .Where(cm => cm.ConversationId == message.ConversationId && cm.UserId == userId)
                .Select(cm => (ConversationMemberRole?)cm.Role)
                .FirstOrDefaultAsync();

            canDelete = actorRole is ConversationMemberRole.Owner or ConversationMemberRole.Admin;
        }

        if (!canDelete)
            throw new UnauthorizedAccessException("You can only delete your own messages.");

        List<string> mediaUrlsToDelete = new();
        if (!message.IsDeleted)
        {
            foreach (var attachment in message.Attachments)
            {
                if (!string.IsNullOrWhiteSpace(attachment.StorageUrl))
                    mediaUrlsToDelete.Add(attachment.StorageUrl);
                if (!string.IsNullOrWhiteSpace(attachment.ThumbnailUrl))
                    mediaUrlsToDelete.Add(attachment.ThumbnailUrl);
            }

            message.IsDeleted = true;
            message.Content = string.Empty;
            message.IsPinned = false;
            message.PinnedById = null;
            message.PinnedAt = null;
            await _context.SaveChangesAsync();

            if (_mediaStorageService != null && mediaUrlsToDelete.Count > 0)
            {
                foreach (var url in mediaUrlsToDelete)
                {
                    try
                    {
                        var key = _mediaStorageService.GetKeyFromUrl(url);
                        if (!string.IsNullOrWhiteSpace(key))
                        {
                            await _mediaStorageService.DeleteAsync(key, CancellationToken.None);
                        }
                    }
                    catch
                    {
                        // Best-effort media cleanup on soft delete
                    }
                }
            }
        }

        return new MessageDeletedDto(message.Id, message.ConversationId);
    }

    public async Task<MessageReadDto> MarkAsReadAsync(Guid conversationId, Guid userId, MarkReadRequest request)
    {
        var member = await _context.ConversationMembers
            .FirstOrDefaultAsync(cm => cm.ConversationId == conversationId && cm.UserId == userId)
            ?? throw new UnauthorizedAccessException("Not a member of this conversation.");

        var targetMessage = await _context.Messages
            .FirstOrDefaultAsync(m => m.Id == request.MessageId && m.ConversationId == conversationId)
            ?? throw new KeyNotFoundException("Message not found in this conversation.");

        member.LastReadMessageId = request.MessageId;
        member.LastReadAt = targetMessage.CreatedAt;

        await _context.SaveChangesAsync();

        return new MessageReadDto(conversationId, userId, request.MessageId, member.LastReadAt.Value);
    }

    public async Task<IReadOnlyList<Guid>> GetUserConversationIdsAsync(Guid userId)
    {
        return await _context.ConversationMembers
            .Where(cm => cm.UserId == userId)
            .Select(cm => cm.ConversationId)
            .ToListAsync();
    }

    public async Task<bool> CanMessageUserAsync(Guid senderId, Guid recipientId)
    {
        if (senderId == recipientId)
            return false;

        var recipient = await _context.Users.FindAsync(recipientId);
        return recipient != null && recipient.IsActive;
    }

    public async Task<bool> IsMemberAsync(Guid conversationId, Guid userId)
    {
        return await _context.ConversationMembers
            .AnyAsync(cm => cm.ConversationId == conversationId && cm.UserId == userId);
    }

    private async Task<HashSet<Guid>> GetOnlineUserSetAsync(IEnumerable<Guid> userIds)
    {
        if (_presenceService == null)
            return new HashSet<Guid>();

        var distinctIds = userIds.Distinct().ToList();
        if (distinctIds.Count == 0)
            return new HashSet<Guid>();

        var online = await _presenceService.GetOnlineUsersAsync(distinctIds);
        return online.ToHashSet();
    }

    internal static IQueryable<Message> IncludeFullMessageDetails(IQueryable<Message> query)
    {
        return query
            .Include(m => m.Sender)
            .Include(m => m.Attachments)
            .Include(m => m.Reactions)
                .ThenInclude(r => r.User)
            .Include(m => m.ReplyToMessage)
                .ThenInclude(r => r!.Sender)
            .Include(m => m.ReplyToMessage)
                .ThenInclude(r => r!.Attachments)
            .Include(m => m.Poll)
                .ThenInclude(p => p!.CreatedBy)
            .Include(m => m.Poll)
                .ThenInclude(p => p!.Options)
                    .ThenInclude(o => o.Votes);
    }

    internal static string? SerializeMentionedUserIds(List<Guid>? mentionedUserIds)
    {
        if (mentionedUserIds == null || mentionedUserIds.Count == 0)
            return null;

        var distinct = mentionedUserIds
            .Where(id => id != Guid.Empty)
            .Distinct()
            .ToList();

        if (distinct.Count == 0)
            return null;

        var json = JsonSerializer.Serialize(distinct);
        if (json.Length > 4000)
            throw new ArgumentException("Too many mentioned users in a single message.");

        return json;
    }

    internal static List<Guid> DeserializeMentionedUserIds(string? json)
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

    internal static ConversationDto MapConversationToDto(
        Conversation conv,
        Guid currentUserId,
        HashSet<Guid> onlineUserIds,
        int unreadCount,
        int pinnedCount)
    {
        var lastMsg = conv.Messages.OrderByDescending(m => m.CreatedAt).FirstOrDefault();
        var currentMember = conv.Members.FirstOrDefault(m => m.UserId == currentUserId);

        return new ConversationDto
        {
            Id = conv.Id,
            Type = conv.Type.ToString(),
            Name = conv.Name,
            AvatarUrl = conv.AvatarUrl,
            CreatedById = conv.CreatedById,
            InviteCode = conv.IsInviteLinkEnabled ? conv.InviteCode : null,
            IsInviteLinkEnabled = conv.IsInviteLinkEnabled,
            RequireJoinApproval = conv.RequireJoinApproval,
            OnlyAdminsCanSend = conv.OnlyAdminsCanSend,
            IsDisbanded = conv.IsDisbanded,
            PinnedCount = pinnedCount,
            MutedUntil = currentMember?.MutedUntil,
            CurrentUserRole = currentMember?.Role.ToString(),
            Members = conv.Members.Select(m => new ConversationMemberDto
            {
                UserId = m.UserId,
                Username = m.User?.Username ?? string.Empty,
                DisplayName = m.User?.DisplayName ?? string.Empty,
                AvatarUrl = m.User?.AvatarUrl,
                Role = m.Role.ToString(),
                Nickname = m.Nickname,
                MutedUntil = m.MutedUntil,
                IsOnline = onlineUserIds.Contains(m.UserId),
                LastSeenAt = m.User?.LastSeenAt,
                LastReadAt = m.LastReadAt
            }).ToList(),
            LastMessage = lastMsg != null ? MapMessageToDto(lastMsg, currentUserId) : null,
            UnreadCount = unreadCount,
            CreatedAt = conv.CreatedAt,
            UpdatedAt = conv.UpdatedAt
        };
    }

    internal static MessageDto MapMessageToDto(Message msg, Guid? currentUserId = null)
    {
        return new MessageDto
        {
            Id = msg.Id,
            ConversationId = msg.ConversationId,
            SenderId = msg.SenderId,
            SenderUsername = msg.Sender?.Username ?? string.Empty,
            SenderDisplayName = msg.Sender?.DisplayName ?? string.Empty,
            Type = msg.Type.ToString(),
            Content = msg.IsDeleted ? DeletedMessagePlaceholder : msg.Content,
            ClientMessageId = msg.ClientMessageId,
            IsDeleted = msg.IsDeleted,
            CreatedAt = msg.CreatedAt,
            EditedAt = msg.EditedAt,
            ReplyToMessageId = msg.ReplyToMessageId,
            ReplyTo = msg.ReplyToMessage != null ? MapReplyPreview(msg.ReplyToMessage) : null,
            MentionedUserIds = msg.IsDeleted ? new List<Guid>() : DeserializeMentionedUserIds(msg.MentionedUserIdsJson),
            MentionAll = !msg.IsDeleted && msg.MentionAll,
            IsPinned = msg.IsPinned,
            PinnedById = msg.PinnedById,
            PinnedAt = msg.PinnedAt,
            Attachments = msg.IsDeleted
                ? new List<MessageAttachmentDto>()
                : msg.Attachments
                    .OrderBy(a => a.SortOrder)
                    .Select(MapAttachmentToDto)
                    .ToList(),
            Reactions = msg.IsDeleted
                ? new List<MessageReactionSummaryDto>()
                : MapReactions(msg.Reactions, currentUserId),
            Poll = !msg.IsDeleted && msg.Poll != null
                ? MapPollToDto(msg.Poll, msg.Id, currentUserId)
                : null
        };
    }

    internal static ReplyMessagePreviewDto MapReplyPreview(Message reply)
    {
        string? thumbnailUrl = null;
        if (!reply.IsDeleted && reply.Attachments.Count > 0)
        {
            var first = reply.Attachments.OrderBy(a => a.SortOrder).First();
            thumbnailUrl = first.ThumbnailUrl ?? (first.AttachmentType == AttachmentType.Image ? first.StorageUrl : null);
        }

        return new ReplyMessagePreviewDto
        {
            Id = reply.Id,
            SenderId = reply.SenderId,
            SenderUsername = reply.Sender?.Username ?? string.Empty,
            SenderDisplayName = reply.Sender?.DisplayName ?? string.Empty,
            Type = reply.Type.ToString(),
            Content = reply.IsDeleted ? DeletedMessagePlaceholder : reply.Content,
            IsDeleted = reply.IsDeleted,
            ThumbnailUrl = thumbnailUrl
        };
    }

    internal static MessageAttachmentDto MapAttachmentToDto(MessageAttachment attachment)
    {
        return new MessageAttachmentDto
        {
            Id = attachment.Id,
            AttachmentType = attachment.AttachmentType.ToString(),
            StorageUrl = attachment.StorageUrl,
            ThumbnailUrl = attachment.ThumbnailUrl,
            MimeType = attachment.MimeType,
            OriginalFileName = attachment.OriginalFileName,
            FileSizeBytes = attachment.FileSizeBytes,
            DurationSeconds = attachment.DurationSeconds,
            WaveformJson = attachment.WaveformJson,
            Width = attachment.Width,
            Height = attachment.Height,
            SortOrder = attachment.SortOrder
        };
    }

    internal static List<MessageReactionSummaryDto> MapReactions(IEnumerable<MessageReaction> reactions, Guid? currentUserId)
    {
        return reactions
            .GroupBy(r => r.Emoji, StringComparer.Ordinal)
            .Select(g => new MessageReactionSummaryDto
            {
                Emoji = g.Key,
                Count = g.Count(),
                UserIds = g.Select(r => r.UserId).Distinct().ToList(),
                ReactedByCurrentUser = currentUserId.HasValue && g.Any(r => r.UserId == currentUserId.Value)
            })
            .OrderByDescending(r => r.Count)
            .ThenBy(r => r.Emoji, StringComparer.Ordinal)
            .ToList();
    }

    internal static ChatPollDto MapPollToDto(ChatPoll poll, Guid? messageId, Guid? currentUserId)
    {
        var orderedOptions = poll.Options
            .OrderBy(o => o.SortOrder)
            .ToList();

        var optionDtos = orderedOptions.Select(o =>
        {
            var votes = o.Votes ?? Enumerable.Empty<ChatPollVote>();
            var voterIds = votes.Select(v => v.UserId).Distinct().ToList();
            return new ChatPollOptionDto
            {
                Id = o.Id,
                Text = o.Text,
                SortOrder = o.SortOrder,
                VoteCount = voterIds.Count,
                VotedByCurrentUser = currentUserId.HasValue && voterIds.Contains(currentUserId.Value),
                VoterIds = poll.IsAnonymous ? new List<Guid>() : voterIds
            };
        }).ToList();

        var allVoterIds = orderedOptions
            .SelectMany(o => o.Votes ?? Enumerable.Empty<ChatPollVote>())
            .Select(v => v.UserId)
            .Distinct()
            .Count();

        var isEffectivelyClosed = poll.IsClosed || (poll.ExpiresAt.HasValue && poll.ExpiresAt.Value <= DateTime.UtcNow);

        return new ChatPollDto
        {
            Id = poll.Id,
            ConversationId = poll.ConversationId,
            MessageId = messageId,
            CreatedById = poll.CreatedById,
            CreatedByDisplayName = poll.CreatedBy?.DisplayName ?? string.Empty,
            Question = poll.Question,
            AllowMultipleChoices = poll.AllowMultipleChoices,
            IsAnonymous = poll.IsAnonymous,
            IsClosed = isEffectivelyClosed,
            ExpiresAt = poll.ExpiresAt,
            ClosedAt = poll.ClosedAt,
            CreatedAt = poll.CreatedAt,
            TotalVotes = optionDtos.Sum(o => o.VoteCount),
            TotalVoters = allVoterIds,
            Options = optionDtos
        };
    }
}
