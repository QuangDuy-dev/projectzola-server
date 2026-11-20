using Microsoft.EntityFrameworkCore;
using MySocialApp.Application.DTOs.Chat;
using MySocialApp.Application.Interfaces;
using MySocialApp.Domain.Entities;
using MySocialApp.Domain.Enums;

namespace MySocialApp.Application.Services;

public class ChatInteractionService : IChatInteractionService
{
    private readonly IApplicationDbContext _context;

    public ChatInteractionService(IApplicationDbContext context)
    {
        _context = context;
    }

    public async Task<MessageReactionEventDto> ToggleReactionAsync(
        Guid conversationId,
        Guid messageId,
        Guid userId,
        ToggleReactionRequest request,
        CancellationToken ct = default)
    {
        var (conversation, _) = await EnsureMemberAndActiveConversationAsync(conversationId, userId, ct);

        var emoji = request?.Emoji?.Trim();
        if (string.IsNullOrWhiteSpace(emoji))
            throw new ArgumentException("Emoji cannot be empty.");

        if (emoji.Length > 16)
            throw new ArgumentException("Emoji cannot exceed 16 characters.");

        var message = await _context.Messages
            .FirstOrDefaultAsync(m => m.Id == messageId && m.ConversationId == conversationId, ct)
            ?? throw new KeyNotFoundException("Message not found in this conversation.");

        if (message.IsDeleted)
            throw new InvalidOperationException("Cannot react to a deleted message.");

        var reactions = await _context.MessageReactions
            .Where(r => r.MessageId == messageId)
            .ToListAsync(ct);

        var existing = reactions.FirstOrDefault(r =>
            r.UserId == userId && string.Equals(r.Emoji, emoji, StringComparison.Ordinal));

        bool added;
        if (existing != null)
        {
            _context.MessageReactions.Remove(existing);
            reactions.Remove(existing);
            added = false;
        }
        else
        {
            var newReaction = new MessageReaction
            {
                MessageId = messageId,
                UserId = userId,
                Emoji = emoji
            };
            _context.MessageReactions.Add(newReaction);
            reactions.Add(newReaction);
            added = true;
        }

        await _context.SaveChangesAsync(ct);

        return new MessageReactionEventDto
        {
            ConversationId = conversation.Id,
            MessageId = messageId,
            UserId = userId,
            Emoji = emoji,
            Added = added,
            Reactions = ChatService.MapReactions(reactions, userId)
        };
    }

    public async Task<MessagePinEventDto> PinMessageAsync(
        Guid conversationId,
        Guid messageId,
        Guid userId,
        CancellationToken ct = default)
    {
        var (conversation, member) = await EnsureMemberAndActiveConversationAsync(conversationId, userId, ct);
        EnsureCanManagePins(conversation, member);

        var message = await ChatService.IncludeFullMessageDetails(_context.Messages)
            .FirstOrDefaultAsync(m => m.Id == messageId && m.ConversationId == conversationId, ct)
            ?? throw new KeyNotFoundException("Message not found in this conversation.");

        if (message.IsDeleted)
            throw new InvalidOperationException("Cannot pin a deleted message.");

        if (!message.IsPinned)
        {
            message.IsPinned = true;
            message.PinnedById = userId;
            message.PinnedAt = DateTime.UtcNow;
            await _context.SaveChangesAsync(ct);
        }

        return new MessagePinEventDto
        {
            ConversationId = conversationId,
            MessageId = message.Id,
            IsPinned = true,
            PinnedById = message.PinnedById,
            PinnedAt = message.PinnedAt,
            Message = ChatService.MapMessageToDto(message, userId)
        };
    }

    public async Task<MessagePinEventDto> UnpinMessageAsync(
        Guid conversationId,
        Guid messageId,
        Guid userId,
        CancellationToken ct = default)
    {
        var (conversation, member) = await EnsureMemberAndActiveConversationAsync(conversationId, userId, ct);
        EnsureCanManagePins(conversation, member);

        var message = await ChatService.IncludeFullMessageDetails(_context.Messages)
            .FirstOrDefaultAsync(m => m.Id == messageId && m.ConversationId == conversationId, ct)
            ?? throw new KeyNotFoundException("Message not found in this conversation.");

        if (message.IsPinned)
        {
            message.IsPinned = false;
            message.PinnedById = null;
            message.PinnedAt = null;
            await _context.SaveChangesAsync(ct);
        }

        return new MessagePinEventDto
        {
            ConversationId = conversationId,
            MessageId = message.Id,
            IsPinned = false,
            PinnedById = null,
            PinnedAt = null,
            Message = ChatService.MapMessageToDto(message, userId)
        };
    }

    public async Task<IReadOnlyList<MessageDto>> GetPinnedMessagesAsync(
        Guid conversationId,
        Guid userId,
        CancellationToken ct = default)
    {
        var isMember = await _context.ConversationMembers
            .AnyAsync(cm => cm.ConversationId == conversationId && cm.UserId == userId, ct);

        if (!isMember)
            throw new UnauthorizedAccessException("Not a member of this conversation.");

        var pinnedMessages = await ChatService.IncludeFullMessageDetails(_context.Messages)
            .Where(m => m.ConversationId == conversationId && m.IsPinned && !m.IsDeleted)
            .OrderByDescending(m => m.PinnedAt)
            .ThenByDescending(m => m.CreatedAt)
            .ToListAsync(ct);

        return pinnedMessages.Select(m => ChatService.MapMessageToDto(m, userId)).ToList();
    }

    public async Task<MessageDto> CreatePollAsync(
        Guid conversationId,
        Guid userId,
        CreatePollRequest request,
        CancellationToken ct = default)
    {
        if (request == null)
            throw new ArgumentException("Poll request is required.");

        var (conversation, member) = await EnsureMemberAndActiveConversationAsync(conversationId, userId, ct);

        if (conversation.Type == ConversationType.Group
            && conversation.OnlyAdminsCanSend
            && member.Role is not (ConversationMemberRole.Owner or ConversationMemberRole.Admin))
        {
            throw new UnauthorizedAccessException("Only group owners and admins can create polls in this group.");
        }

        // Idempotency check using ClientMessageId
        if (!string.IsNullOrWhiteSpace(request.ClientMessageId))
        {
            var existingMessage = await ChatService.IncludeFullMessageDetails(_context.Messages)
                .FirstOrDefaultAsync(m => m.ConversationId == conversationId
                                          && m.SenderId == userId
                                          && m.ClientMessageId == request.ClientMessageId, ct);

            if (existingMessage != null)
                return ChatService.MapMessageToDto(existingMessage, userId);
        }

        var question = request.Question?.Trim();
        if (string.IsNullOrWhiteSpace(question))
            throw new ArgumentException("Poll question is required.");

        if (question.Length > 300)
            throw new ArgumentException("Poll question cannot exceed 300 characters.");

        if (request.Options == null || request.Options.Count < 2 || request.Options.Count > 10)
            throw new ArgumentException("A poll must have between 2 and 10 options.");

        var cleanedOptions = new List<string>(request.Options.Count);
        foreach (var rawOption in request.Options)
        {
            var text = rawOption?.Trim();
            if (string.IsNullOrWhiteSpace(text))
                throw new ArgumentException("Poll options cannot be empty.");

            if (text.Length > 200)
                throw new ArgumentException("Each poll option cannot exceed 200 characters.");

            cleanedOptions.Add(text);
        }

        if (request.ExpiresAt.HasValue && request.ExpiresAt.Value <= DateTime.UtcNow)
            throw new ArgumentException("Poll expiration time must be in the future.");

        var poll = new ChatPoll
        {
            ConversationId = conversationId,
            CreatedById = userId,
            Question = question,
            AllowMultipleChoices = request.AllowMultipleChoices,
            IsAnonymous = request.IsAnonymous,
            IsClosed = false,
            ExpiresAt = request.ExpiresAt
        };

        for (int i = 0; i < cleanedOptions.Count; i++)
        {
            poll.Options.Add(new ChatPollOption
            {
                PollId = poll.Id,
                Text = cleanedOptions[i],
                SortOrder = i
            });
        }

        var message = new Message
        {
            ConversationId = conversationId,
            SenderId = userId,
            Type = MessageType.Poll,
            Content = question,
            PollId = poll.Id,
            Poll = poll,
            ClientMessageId = request.ClientMessageId
        };

        _context.ChatPolls.Add(poll);
        _context.Messages.Add(message);
        conversation.UpdatedAt = DateTime.UtcNow;

        await _context.SaveChangesAsync(ct);

        var saved = await ChatService.IncludeFullMessageDetails(_context.Messages)
            .FirstAsync(m => m.Id == message.Id, ct);

        return ChatService.MapMessageToDto(saved, userId);
    }

    public async Task<PollUpdatedEventDto> VotePollAsync(
        Guid conversationId,
        Guid pollId,
        Guid userId,
        VotePollRequest request,
        CancellationToken ct = default)
    {
        await EnsureMemberAndActiveConversationAsync(conversationId, userId, ct);

        var poll = await _context.ChatPolls
            .Include(p => p.Options)
            .FirstOrDefaultAsync(p => p.Id == pollId && p.ConversationId == conversationId, ct)
            ?? throw new KeyNotFoundException("Poll not found in this conversation.");

        if (poll.IsClosed)
            throw new InvalidOperationException("This poll is already closed.");

        if (poll.ExpiresAt.HasValue && poll.ExpiresAt.Value <= DateTime.UtcNow)
            throw new InvalidOperationException("This poll has expired.");

        var targetOptionIds = (request?.OptionIds ?? new List<Guid>())
            .Where(id => id != Guid.Empty)
            .Distinct()
            .ToList();

        if (!poll.AllowMultipleChoices && targetOptionIds.Count > 1)
            throw new ArgumentException("This poll only allows selecting a single option.");

        var validOptionIds = poll.Options.Select(o => o.Id).ToHashSet();
        if (targetOptionIds.Any(id => !validOptionIds.Contains(id)))
            throw new ArgumentException("One or more selected options do not belong to this poll.");

        var existingVotes = await _context.ChatPollVotes
            .Where(v => v.PollId == pollId && v.UserId == userId)
            .ToListAsync(ct);

        var targetSet = targetOptionIds.ToHashSet();
        var votesToRemove = existingVotes.Where(v => !targetSet.Contains(v.OptionId)).ToList();
        var existingOptionSet = existingVotes.Select(v => v.OptionId).ToHashSet();
        var optionIdsToAdd = targetOptionIds.Where(id => !existingOptionSet.Contains(id)).ToList();

        if (votesToRemove.Count > 0)
            _context.ChatPollVotes.RemoveRange(votesToRemove);

        foreach (var optionId in optionIdsToAdd)
        {
            _context.ChatPollVotes.Add(new ChatPollVote
            {
                PollId = pollId,
                OptionId = optionId,
                UserId = userId
            });
        }

        if (votesToRemove.Count > 0 || optionIdsToAdd.Count > 0)
        {
            await _context.SaveChangesAsync(ct);
        }

        var updatedPoll = await LoadPollForDtoAsync(conversationId, pollId, ct);
        var messageId = await GetPollMessageIdAsync(conversationId, pollId, ct);

        return new PollUpdatedEventDto
        {
            ConversationId = conversationId,
            MessageId = messageId,
            Poll = ChatService.MapPollToDto(updatedPoll, messageId, userId)
        };
    }

    public async Task<PollUpdatedEventDto> ClosePollAsync(
        Guid conversationId,
        Guid pollId,
        Guid userId,
        CancellationToken ct = default)
    {
        var (conversation, member) = await EnsureMemberAndActiveConversationAsync(conversationId, userId, ct);

        var poll = await _context.ChatPolls
            .FirstOrDefaultAsync(p => p.Id == pollId && p.ConversationId == conversationId, ct)
            ?? throw new KeyNotFoundException("Poll not found in this conversation.");

        var isCreator = poll.CreatedById == userId;
        var isGroupAdmin = conversation.Type == ConversationType.Group
                           && member.Role is ConversationMemberRole.Owner or ConversationMemberRole.Admin;

        if (!isCreator && !isGroupAdmin)
            throw new UnauthorizedAccessException("Only the poll creator or group admins can close this poll.");

        if (!poll.IsClosed)
        {
            poll.IsClosed = true;
            poll.ClosedAt = DateTime.UtcNow;
            await _context.SaveChangesAsync(ct);
        }

        var updatedPoll = await LoadPollForDtoAsync(conversationId, pollId, ct);
        var messageId = await GetPollMessageIdAsync(conversationId, pollId, ct);

        return new PollUpdatedEventDto
        {
            ConversationId = conversationId,
            MessageId = messageId,
            Poll = ChatService.MapPollToDto(updatedPoll, messageId, userId)
        };
    }

    public async Task<ChatPollDto> GetPollAsync(
        Guid conversationId,
        Guid pollId,
        Guid userId,
        CancellationToken ct = default)
    {
        var isMember = await _context.ConversationMembers
            .AnyAsync(cm => cm.ConversationId == conversationId && cm.UserId == userId, ct);

        if (!isMember)
            throw new UnauthorizedAccessException("Not a member of this conversation.");

        var poll = await LoadPollForDtoAsync(conversationId, pollId, ct);
        var messageId = await GetPollMessageIdAsync(conversationId, pollId, ct);

        return ChatService.MapPollToDto(poll, messageId, userId);
    }

    private async Task<(Conversation Conversation, ConversationMember Member)> EnsureMemberAndActiveConversationAsync(
        Guid conversationId,
        Guid userId,
        CancellationToken ct)
    {
        var member = await _context.ConversationMembers
            .Include(cm => cm.Conversation)
            .FirstOrDefaultAsync(cm => cm.ConversationId == conversationId && cm.UserId == userId, ct);

        if (member == null)
            throw new UnauthorizedAccessException("Not a member of this conversation.");

        if (member.Conversation.IsDisbanded)
            throw new InvalidOperationException("This group has been disbanded.");

        return (member.Conversation, member);
    }

    private static void EnsureCanManagePins(Conversation conversation, ConversationMember member)
    {
        if (conversation.Type == ConversationType.Group
            && member.Role is not (ConversationMemberRole.Owner or ConversationMemberRole.Admin))
        {
            throw new UnauthorizedAccessException("Only group owners and admins can pin or unpin messages.");
        }
    }

    private async Task<ChatPoll> LoadPollForDtoAsync(Guid conversationId, Guid pollId, CancellationToken ct)
    {
        return await _context.ChatPolls
            .AsNoTracking()
            .Include(p => p.CreatedBy)
            .Include(p => p.Options)
                .ThenInclude(o => o.Votes)
            .FirstOrDefaultAsync(p => p.Id == pollId && p.ConversationId == conversationId, ct)
            ?? throw new KeyNotFoundException("Poll not found in this conversation.");
    }

    private async Task<Guid?> GetPollMessageIdAsync(Guid conversationId, Guid pollId, CancellationToken ct)
    {
        return await _context.Messages
            .Where(m => m.ConversationId == conversationId && m.PollId == pollId)
            .Select(m => (Guid?)m.Id)
            .FirstOrDefaultAsync(ct);
    }
}
