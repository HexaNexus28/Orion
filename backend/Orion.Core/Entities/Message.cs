namespace Orion.Core.Entities;

public class Message
{
    public Guid Id { get; set; }
    public Guid ConversationId { get; set; }
    public Enums.MessageRole Role { get; set; }
    public string Content { get; set; } = string.Empty;
    public string? ToolName { get; set; }
    public string? ToolInput { get; set; } // JSON string
    public string? ToolResult { get; set; } // JSON string
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    
    // Navigation property
    public Conversation Conversation { get; set; } = null!;
}
