namespace MySocialApp.Application.DTOs.Chatbot;

public class ChatbotChunk
{
    public string EventType { get; set; } = "token"; // "status", "token", "done", "error"
    public string? Text { get; set; }
    public string? State { get; set; }
    public string? Category { get; set; }
    public string? Error { get; set; }
    public long? DurationMs { get; set; }
    public long? TimeToFirstTokenMs { get; set; }
    public int? TotalTokens { get; set; }

    public static ChatbotChunk Status(string state) => new() { EventType = "status", State = state };
    public static ChatbotChunk Token(string text) => new() { EventType = "token", Text = text };
    public static ChatbotChunk Done(string category, long durationMs, long ttftMs, int tokens) => new()
    {
        EventType = "done",
        Category = category,
        DurationMs = durationMs,
        TimeToFirstTokenMs = ttftMs,
        TotalTokens = tokens
    };
    public static ChatbotChunk ErrorEvent(string error) => new() { EventType = "error", Error = error };
}
