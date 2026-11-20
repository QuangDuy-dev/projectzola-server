using MySocialApp.Domain.Common;
using MySocialApp.Domain.Enums;

namespace MySocialApp.Domain.Entities;

public class GroupJoinRequest : BaseEntity
{
    public Guid ConversationId { get; set; }
    public Conversation Conversation { get; set; } = null!;
    public Guid UserId { get; set; }
    public User User { get; set; } = null!;
    public Guid? RequestedById { get; set; }
    public User? RequestedBy { get; set; }
    public JoinRequestStatus Status { get; set; } = JoinRequestStatus.Pending;
    public Guid? ReviewedById { get; set; }
    public User? ReviewedBy { get; set; }
    public DateTime? ReviewedAt { get; set; }
}
