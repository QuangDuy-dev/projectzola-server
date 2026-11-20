using System.Text;
using System.Text.Json;

namespace MySocialApp.Application.Common;

public class FeedCursor
{
    public DateTime CreatedAt { get; set; }
    public Guid PostId { get; set; }
    public bool IsFallback { get; set; } = false;
}

public static class FeedCursorCodec
{
    public static string Encode(DateTime createdAt, Guid postId, bool isFallback = false)
    {
        var cursor = new FeedCursor
        {
            CreatedAt = createdAt,
            PostId = postId,
            IsFallback = isFallback
        };
        var json = JsonSerializer.Serialize(cursor);
        var bytes = Encoding.UTF8.GetBytes(json);
        return Convert.ToBase64String(bytes)
            .TrimEnd('=')
            .Replace('+', '-')
            .Replace('/', '_');
    }

    public static FeedCursor? Decode(string? cursorStr)
    {
        if (string.IsNullOrWhiteSpace(cursorStr))
            return null;

        try
        {
            var incoming = cursorStr.Trim().Replace('-', '+').Replace('_', '/');
            switch (incoming.Length % 4)
            {
                case 2: incoming += "=="; break;
                case 3: incoming += "="; break;
            }

            var bytes = Convert.FromBase64String(incoming);
            var json = Encoding.UTF8.GetString(bytes);
            var cursor = JsonSerializer.Deserialize<FeedCursor>(json);

            if (cursor == null || cursor.CreatedAt == default || cursor.PostId == Guid.Empty)
            {
                throw new ArgumentException("Invalid cursor format.");
            }

            return cursor;
        }
        catch (Exception ex) when (ex is not ArgumentException)
        {
            throw new ArgumentException("Invalid cursor format.", ex);
        }
    }
}
