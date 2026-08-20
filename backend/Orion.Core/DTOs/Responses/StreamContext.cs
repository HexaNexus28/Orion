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

    /// <summary>Des souvenirs RAG ont réellement été injectés dans le prompt.</summary>
    public bool MemoryUsed { get; set; }
}
