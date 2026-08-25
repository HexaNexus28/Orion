using Orion.Core.DTOs.Requests;
using Orion.Core.DTOs.Responses;

namespace Orion.Core.Interfaces.Agents;

public interface IConversationAgent
{
    /// <summary>
    /// Tour complet, réponse agrégée. Passe par la MÊME boucle agent que le streaming —
    /// c'est ce qui empêche les deux chemins de diverger à nouveau.
    /// </summary>
    Task<ApiResponse<ChatResponse>> ProcessAsync(ChatRequest request, CancellationToken ct = default);

    // Streaming en 2 étapes (ApiResponse pattern) :
    // 1. PrepareStreamAsync — init session, sauvegarde le message utilisateur, construit le prompt
    // 2. StreamLLMAsync — déroule la boucle agent et sauvegarde la réponse
    Task<ApiResponse<StreamContext>> PrepareStreamAsync(ChatRequest request, CancellationToken ct = default);

    IAsyncEnumerable<AgentEvent> StreamLLMAsync(StreamContext context, CancellationToken ct = default);

    /// <summary>Prepare + stream combinés (utilisé par VoiceController).</summary>
    IAsyncEnumerable<AgentEvent> StreamAsync(ChatRequest request, CancellationToken ct = default);
}
