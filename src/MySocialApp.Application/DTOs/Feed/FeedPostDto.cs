namespace MySocialApp.Application.DTOs.Feed;

public class FeedPostDto
{
    public Guid Id { get; set; }
    public FeedAuthorDto Author { get; set; } = null!;
    public string Content { get; set; } = string.Empty;
    public List<FeedMediaDto> Media { get; set; } = new();
    public int LikeCount { get; set; }
    public int DislikeCount { get; set; }
    public int CommentCount { get; set; }
    public string? CurrentUserReaction { get; set; }
    public bool IsFollowingAuthor { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime? EditedAt { get; set; }
}

public class FeedAuthorDto
{
    public Guid Id { get; set; }
    public string Username { get; set; } = string.Empty;
    public string DisplayName { get; set; } = string.Empty;
    public string? AvatarUrl { get; set; }
}

public class FeedMediaDto
{
    public Guid Id { get; set; }
    public string MediaType { get; set; } = string.Empty;
    public string Url { get; set; } = string.Empty;
    public string? ThumbnailUrl { get; set; }
}
