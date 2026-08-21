using Orion.Core.DTOs.Requests;

namespace Orion.Core.DTOs.Responses;

/// <summary>
/// Contexte préparé pour la boucle agent.
/// Retourné par PrepareStreamAsync (ApiResponse pattern)
/// puis consommé par StreamLLMAsync (IAsyncEnumerable).
/// </summary>
public class StreamContext
{
    public Guid SessionId { get; set; }
    public Guid ConversationId { get; set; }
    public LLMRequest LlmRequest { get; set; } = default!;

    /// <summary>
    /// La demande telle que l'utilisateur l'a formulée. Sert si un outil doit être différé :
    /// au réveil du PC, ORION doit pouvoir rappeler la phrase exacte, pas un nom d'outil.
    /// </summary>
    public string UserMessage { get; set; } = string.Empty;

    /// <summary>Des souvenirs RAG ont réellement été injectés dans le prompt.</summary>
    public bool MemoryUsed { get; set; }
}
