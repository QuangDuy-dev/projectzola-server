using MySocialApp.Domain.Common;
using MySocialApp.Domain.Enums;

namespace MySocialApp.Domain.Entities;

public class Message : BaseEntity
{
    public Guid ConversationId { get; set; }
    public Conversation Conversation { get; set; } = null!;
    public Guid SenderId { get; set; }
    public User Sender { get; set; } = null!;
    public string Content { get; set; } = string.Empty;
    public string? ClientMessageId { get; set; }
    public DateTime? EditedAt { get; set; }
    public bool IsDeleted { get; set; } = false;

    public MessageType Type { get; set; } = MessageType.Text;
    public Guid? ReplyToMessageId { get; set; }
    public Message? ReplyToMessage { get; set; }
    public string? MentionedUserIdsJson { get; set; }
    public bool MentionAll { get; set; } = false;
    public bool IsPinned { get; set; } = false;
    public Guid? PinnedById { get; set; }
    public User? PinnedBy { get; set; }
    public DateTime? PinnedAt { get; set; }
    public Guid? PollId { get; set; }
    public ChatPoll? Poll { get; set; }

    public ICollection<MessageAttachment> Attachments { get; set; } = new List<MessageAttachment>();
    public ICollection<MessageReaction> Reactions { get; set; } = new List<MessageReaction>();
}
