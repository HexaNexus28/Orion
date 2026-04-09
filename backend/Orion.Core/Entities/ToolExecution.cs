namespace Orion.Core.Entities;

/// <summary>
/// Log des exécutions d'outils (tools)
/// </summary>
public class ToolExecution
{
    public Guid Id { get; set; }
    public Guid? MessageId { get; set; } // Référence optionnelle vers le message
    public string ToolName { get; set; } = string.Empty;
    public string? Input { get; set; } // JSONB
    public string? Result { get; set; } // JSONB
    public string? Status { get; set; }
    public int? DurationMs { get; set; }
    public DateTime ExecutedAt { get; set; } = DateTime.UtcNow;
    
    // Navigation property
    public Message? Message { get; set; }
}
