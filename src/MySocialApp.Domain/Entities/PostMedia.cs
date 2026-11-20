using MySocialApp.Domain.Common;
using MySocialApp.Domain.Enums;

namespace MySocialApp.Domain.Entities;

public class PostMedia : BaseEntity
{
    public Guid PostId { get; set; }
    public Post Post { get; set; } = null!;
    public MediaType MediaType { get; set; }
    public string StorageUrl { get; set; } = string.Empty;
    public string? ThumbnailUrl { get; set; }
}
