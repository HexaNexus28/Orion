namespace Orion.Core.DTOs.Responses;

public class ChatResponse
{
    public string Response { get; set; } = string.Empty;
    public Guid SessionId { get; set; }
    public Enums.LLMProvider LlmProvider { get; set; }
    public bool MemoryUsed { get; set; }
    public List<ToolCallDto>? ToolsCalled { get; set; }
}
