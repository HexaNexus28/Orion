namespace Orion.Core.DTOs.Responses;

public class ToolCallDto
{
    public string ToolName { get; set; } = string.Empty;
    public string Input { get; set; } = string.Empty; // JSON
    public string? Result { get; set; } // JSON
}
