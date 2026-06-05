namespace Orion.Core.Configuration;

public class OllamaOptions
{
    public const string SectionName = "Ollama";
    
    public string BaseUrl { get; set; } = "http://localhost:11434";
    public string Model { get; set; } = "deepseek-v4-flash:cloud";
    public string FallbackModel { get; set; } = "llama3.2:3b";
    public int TimeoutSeconds { get; set; } = 120;
}
