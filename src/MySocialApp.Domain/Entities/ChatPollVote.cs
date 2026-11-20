using MySocialApp.Domain.Common;

namespace MySocialApp.Domain.Entities;

public class ChatPollVote : BaseEntity
{
    public Guid PollId { get; set; }
    public ChatPoll Poll { get; set; } = null!;
    public Guid OptionId { get; set; }
    public ChatPollOption Option { get; set; } = null!;
    public Guid UserId { get; set; }
    public User User { get; set; } = null!;
}
