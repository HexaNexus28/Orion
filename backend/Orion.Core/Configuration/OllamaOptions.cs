namespace Orion.Core.Configuration;

public class OllamaOptions
{
    public const string SectionName = "Ollama";
    
    public string BaseUrl { get; set; } = "http://localhost:11434";
    public string Model { get; set; } = "gemma4:31b-cloud";
    public string FallbackModel { get; set; } = "gemma4:31b-cloud";
    public int TimeoutSeconds { get; set; } = 120;
}
