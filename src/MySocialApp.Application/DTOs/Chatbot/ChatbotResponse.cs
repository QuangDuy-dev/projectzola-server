namespace MySocialApp.Application.DTOs.Chatbot;

public class ChatbotResponse
{
    public string Reply { get; set; } = string.Empty;
    public string? Category { get; set; }
    public long DurationMs { get; set; }
    public long? TimeToFirstTokenMs { get; set; }
    public int? OutputTokens { get; set; }
}
