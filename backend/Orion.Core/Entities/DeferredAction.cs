using Orion.Core.Enums;

namespace Orion.Core.Entities;

/// <summary>
/// Une action que l'utilisateur a demandée pendant que son PC était éteint.
///
/// Le daemon n'est pas déportable : ses outils agissent sur CETTE machine. Ce qui est
/// architecturable, ce n'est pas de le déplacer, c'est le comportement d'ORION quand les
/// mains sont absentes — d'où cette ligne, qui remplace un échec sec par une promesse tenue.
/// </summary>
public class DeferredAction
{
    public Guid Id { get; set; } = Guid.NewGuid();

    /// <summary>Nom de l'outil (`open_app`, `git_commit`), pas l'action daemon sous-jacente.</summary>
    public string ToolName { get; set; } = string.Empty;

    /// <summary>Arguments JSON tels que le modèle les a produits, rejoués à l'identique.</summary>
    public string Arguments { get; set; } = "{}";

    public DeferredActionStatus Status { get; set; } = DeferredActionStatus.Pending;

    /// <summary>
    /// Figé à l'enfilement, jamais relu depuis le code de l'outil : une action déjà en file
    /// garde le régime sous lequel elle a été demandée, même si le drapeau change ensuite.
    /// </summary>
    public bool IsDestructive { get; set; }

    /// <summary>'chat' (l'utilisateur a parlé) ou 'proactive' (ORION a décidé seul).</summary>
    public string Origin { get; set; } = "chat";

    public Guid? ConversationId { get; set; }

    /// <summary>La demande telle qu'elle a été formulée, pour pouvoir la rappeler mot pour mot.</summary>
    public string? RequestedBy { get; set; }

    public DateTime RequestedAt { get; set; } = DateTime.UtcNow;
    public DateTime ExpiresAt { get; set; } = DateTime.UtcNow.AddHours(24);
    public DateTime? ResolvedAt { get; set; }

    public string? Result { get; set; }
    public string? Error { get; set; }
}
