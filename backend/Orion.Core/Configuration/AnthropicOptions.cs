namespace Orion.Core.Configuration;

public class AnthropicOptions
{
    public const string SectionName = "Anthropic";
    
    public string ApiKey { get; set; } = string.Empty;
    public string Model { get; set; } = "kimi-k2.5:cloud";
    public int MaxTokens { get; set; } = 4096;
}
