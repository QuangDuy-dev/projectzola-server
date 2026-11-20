using MySocialApp.Domain.Common;
using MySocialApp.Domain.Enums;

namespace MySocialApp.Domain.Entities;

public class Conversation : BaseEntity
{
    public ConversationType Type { get; set; } = ConversationType.Direct;
    public string? Name { get; set; }
    public string? AvatarUrl { get; set; }
    public Guid? CreatedById { get; set; }
    public User? CreatedBy { get; set; }
    public string? InviteCode { get; set; }
    public bool IsInviteLinkEnabled { get; set; } = true;
    public bool RequireJoinApproval { get; set; } = false;
    public bool OnlyAdminsCanSend { get; set; } = false;
    public bool IsDisbanded { get; set; } = false;
    public DateTime? DisbandedAt { get; set; }

    public ICollection<ConversationMember> Members { get; set; } = new List<ConversationMember>();
    public ICollection<Message> Messages { get; set; } = new List<Message>();
    public ICollection<GroupJoinRequest> JoinRequests { get; set; } = new List<GroupJoinRequest>();
    public ICollection<ChatPoll> Polls { get; set; } = new List<ChatPoll>();
}
