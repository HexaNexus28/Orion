namespace Orion.Core.DTOs.Internal.LLM;

/// <summary>
/// DTO pour les réponses de l'API Anthropic
/// </summary>
public class AnthropicResponse
{
    public string? Model { get; set; }
    public List<AnthropicContent>? Content { get; set; }
    public AnthropicUsage? Usage { get; set; }
}

public class AnthropicContent
{
    public string? Type { get; set; }
    public string? Text { get; set; }
}

public class AnthropicUsage
{
    public int InputTokens { get; set; }
    public int OutputTokens { get; set; }
}

public class AnthropicStreamChunk
{
    public AnthropicDelta? Delta { get; set; }
}

public class AnthropicDelta
{
    public string? Text { get; set; }
}
