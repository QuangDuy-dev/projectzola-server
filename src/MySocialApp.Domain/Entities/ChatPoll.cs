using MySocialApp.Domain.Common;

namespace MySocialApp.Domain.Entities;

public class ChatPoll : BaseEntity
{
    public Guid ConversationId { get; set; }
    public Conversation Conversation { get; set; } = null!;
    public Guid CreatedById { get; set; }
    public User CreatedBy { get; set; } = null!;
    public string Question { get; set; } = string.Empty;
    public bool AllowMultipleChoices { get; set; }
    public bool IsAnonymous { get; set; }
    public bool IsClosed { get; set; }
    public DateTime? ExpiresAt { get; set; }
    public DateTime? ClosedAt { get; set; }

    public ICollection<ChatPollOption> Options { get; set; } = new List<ChatPollOption>();
    public ICollection<ChatPollVote> Votes { get; set; } = new List<ChatPollVote>();
}
