namespace Orion.Core.DTOs;

/// <summary>
/// Résumé d'une conversation pour la liste
/// </summary>
public class ConversationSummaryDto
{
    public Guid Id { get; set; }
    public string? Summary { get; set; }
    public DateTime StartedAt { get; set; }
    public DateTime? LastMessageAt { get; set; }
    public int MessageCount { get; set; }
}
