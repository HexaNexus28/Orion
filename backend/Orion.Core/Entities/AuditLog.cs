namespace Orion.Core.Entities;

/// <summary>
/// Log d'audit pour traçabilité des actions
/// </summary>
public class AuditLog
{
    public Guid Id { get; set; }
    public string EntityType { get; set; } = string.Empty; // 'Conversation', 'Message', etc.
    public string EntityId { get; set; } = string.Empty; // UUID de l'entité concernée
    public string Action { get; set; } = string.Empty; // 'Create', 'Update', 'Delete', 'ToolCall', 'LLMCall'
    public string? UserId { get; set; } // Utilisateur qui a fait l'action
    public string? UserName { get; set; }
    public string? OldValues { get; set; } // JSON des valeurs avant
    public string? NewValues { get; set; } // JSON des valeurs après
    public string? Metadata { get; set; } // JSON additionnel (IP, UserAgent, etc.)
    public int? DurationMs { get; set; } // Durée de l'opération en millisecondes
    public bool Success { get; set; } = true;
    public string? ErrorMessage { get; set; }
    public DateTime Timestamp { get; set; } = DateTime.UtcNow;
    public string? CorrelationId { get; set; } // Pour lier les actions d'une même requête
}
