using Orion.Core.DTOs.Requests;
using Orion.Core.DTOs.Responses;

namespace Orion.Core.Interfaces.Agents;

/// <summary>
/// Boucle agent — LE point d'application unique du raisonnement d'ORION.
///
/// Streaming et non-streaming passent tous deux par ici : c'est ce qui empêche la divergence
/// entre les deux chemins de réapparaître (cause racine, docs/jarvis-gap-analysis.md §1.2).
///
/// Le contrat est multi-tours : tant que le LLM demande des outils, la boucle les exécute,
/// réinjecte les résultats et rappelle le modèle — jusqu'à une réponse sans outil ou
/// l'épuisement du budget d'itérations.
/// </summary>
public interface IAgentLoop
{
    IAsyncEnumerable<AgentEvent> RunAsync(
        LLMRequest request,
        Func<string, string, CancellationToken, Task<string>> toolExecutor,
        CancellationToken ct = default);
}
