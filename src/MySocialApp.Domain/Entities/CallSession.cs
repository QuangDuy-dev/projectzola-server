using MySocialApp.Domain.Common;
using MySocialApp.Domain.Enums;

namespace MySocialApp.Domain.Entities;

public class CallSession : BaseEntity
{
    public Guid ConversationId { get; set; }
    public Conversation Conversation { get; set; } = null!;

    public Guid CallerId { get; set; }
    public User Caller { get; set; } = null!;

    public Guid CalleeId { get; set; }
    public User Callee { get; set; } = null!;

    public CallType Type { get; set; } = CallType.Voice;
    public CallStatus Status { get; set; } = CallStatus.Initiating;

    public DateTime? RingingAt { get; set; }
    public DateTime? ConnectedAt { get; set; }
    public DateTime? EndedAt { get; set; }
    public int DurationSeconds { get; set; } = 0;

    /// <summary>
    /// ID of the system/call message generated in the conversation when the call ends.
    /// </summary>
    public Guid? CallLogMessageId { get; set; }
}
