namespace MySocialApp.Application.Options;

public class OllamaOptions
{
    public const string SectionName = "Ollama";

    public string BaseUrl { get; set; } = "http://localhost:11434";
    public string Model { get; set; } = "qwen3.5:4b";
    public string KeepAlive { get; set; } = "5m";
    public int ContextSize { get; set; } = 4096;
    public double Temperature { get; set; } = 0.4;
    public int MaxOutputTokens { get; set; } = 400;
    public int RequestTimeoutSeconds { get; set; } = 120;
    public bool DisableThinking { get; set; } = true;
}
