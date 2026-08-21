namespace Orion.Core.DTOs.Internal.Tools;

/// <summary>
/// D'où vient l'appel d'outil. Sert uniquement si l'action doit être mise en file :
/// sans ce contexte, ORION saurait qu'il doit faire quelque chose au réveil du PC, mais plus
/// à qui le dire ni pourquoi il l'avait promis.
/// </summary>
public record ToolInvocationContext(
    Guid? ConversationId = null,
    string Origin = ToolInvocationContext.OrigineChat,
    string? RequestedBy = null)
{
    public const string OrigineChat = "chat";
    public const string OrigineProactive = "proactive";

    /// <summary>Appel hors conversation (API outils, tests) : rien à rappeler à personne.</summary>
    public static readonly ToolInvocationContext Direct = new();
}
