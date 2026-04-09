namespace Orion.Core.DTOs.Responses;

public class LLMResponse
{
    public string Content { get; set; } = string.Empty;
    public Enums.LLMProvider Provider { get; set; }
    public string Model { get; set; } = string.Empty;
    public int? TokensUsed { get; set; }
    public List<ToolCallDto>? ToolCalls { get; set; }
}
