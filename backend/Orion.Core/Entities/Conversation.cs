namespace Orion.Core.Entities;

public class Conversation
{
    public Guid Id { get; set; }
    public Enums.ConversationType Type { get; set; } = Enums.ConversationType.Chat;
    public DateTime StartedAt { get; set; } = DateTime.UtcNow;
    public DateTime? EndedAt { get; set; }
    public Enums.LLMProvider? LlmProvider { get; set; }
    public string? Summary { get; set; }
    
    // Navigation property
    public ICollection<Message> Messages { get; set; } = new List<Message>();
}
