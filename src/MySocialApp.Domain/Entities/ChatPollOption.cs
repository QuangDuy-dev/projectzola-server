using MySocialApp.Domain.Common;

namespace MySocialApp.Domain.Entities;

public class ChatPollOption : BaseEntity
{
    public Guid PollId { get; set; }
    public ChatPoll Poll { get; set; } = null!;
    public string Text { get; set; } = string.Empty;
    public int SortOrder { get; set; }

    public ICollection<ChatPollVote> Votes { get; set; } = new List<ChatPollVote>();
}
