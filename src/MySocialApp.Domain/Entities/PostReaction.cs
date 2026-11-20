using MySocialApp.Domain.Common;
using MySocialApp.Domain.Enums;

namespace MySocialApp.Domain.Entities;

public class PostReaction : BaseEntity
{
    public Guid PostId { get; set; }
    public Post Post { get; set; } = null!;
    public Guid UserId { get; set; }
    public User User { get; set; } = null!;
    public ReactionType ReactionType { get; set; }
}
