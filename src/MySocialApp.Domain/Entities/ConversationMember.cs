using MySocialApp.Domain.Enums;

namespace MySocialApp.Domain.Entities;

public class ConversationMember
{
    public Guid ConversationId { get; set; }
    public Conversation Conversation { get; set; } = null!;
    public Guid UserId { get; set; }
    public User User { get; set; } = null!;
    public DateTime JoinedAt { get; set; } = DateTime.UtcNow;

    public ConversationMemberRole Role { get; set; } = ConversationMemberRole.Member;
    public string? Nickname { get; set; }
    public DateTime? MutedUntil { get; set; }

    public Guid? LastReadMessageId { get; set; }
    public Message? LastReadMessage { get; set; }
    public DateTime? LastReadAt { get; set; }
}
