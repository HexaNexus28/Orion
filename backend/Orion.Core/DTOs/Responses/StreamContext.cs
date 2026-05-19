using Orion.Core.DTOs.Requests;

namespace Orion.Core.DTOs.Responses;

/// <summary>
/// Contexte préparé pour le streaming LLM.
/// Retourné par PrepareStreamAsync (ApiResponse pattern) 
/// puis consommé par StreamLLMAsync (IAsyncEnumerable).
/// </summary>
public class StreamContext
{
    public Guid SessionId { get; set; }
    public Guid ConversationId { get; set; }
    public LLMRequest LlmRequest { get; set; } = default!;
}
