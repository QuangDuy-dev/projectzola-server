using MySocialApp.Domain.Common;

namespace MySocialApp.Domain.Entities;

public class Post : BaseEntity
{
    public Guid UserId { get; set; }
    public User User { get; set; } = null!;
    public string Content { get; set; } = string.Empty;
    public bool IsDeleted { get; set; } = false;

    public ICollection<PostMedia> Media { get; set; } = new List<PostMedia>();
    public ICollection<PostReaction> Reactions { get; set; } = new List<PostReaction>();
    public ICollection<Comment> Comments { get; set; } = new List<Comment>();
}
