namespace Orion.Core.Configuration;

public class OllamaOptions
{
    public const string SectionName = "Ollama";
    
    public string BaseUrl { get; set; } = "http://localhost:11434";
    public string Model { get; set; } = "qwen2.5:7b";
    public string FallbackModel { get; set; } = "kimi-k2.5:cloud";
    public int TimeoutSeconds { get; set; } = 120;
}
