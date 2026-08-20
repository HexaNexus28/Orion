using Orion.Core.DTOs.Requests;
using Orion.Core.Interfaces.LLM;

namespace Orion.Business.LLM;

public static class LLMAgentClientExtensions
{
    /// <summary>
    /// Un tour de LLM dont on ne veut que le texte final — pas de streaming, pas d'outil.
    /// Évite d'élargir <see cref="ILLMAgentClient"/> pour les appelants « une question,
    /// une réponse » comme la génération de briefing.
    /// </summary>
    public static async Task<string> CompleteTextAsync(
        this ILLMAgentClient client,
        LLMRequest request,
        CancellationToken ct = default)
    {
        var turn = await client.StreamTurnAsync(request, _ => Task.CompletedTask, ct);
        return turn.Content;
    }
}
