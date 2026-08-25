using Orion.Core.DTOs.Requests;
using Orion.Core.DTOs.Responses;
using Orion.Core.Enums;

namespace Orion.Core.Interfaces.LLM;

/// <summary>
/// Contrat d'un transport LLM capable de streamer du texte ET de remonter des appels d'outils.
///
/// Remplace l'ancien `ILLMClient`, dont la signature de streaming (Func&lt;string, Task&gt;)
/// ne pouvait structurellement pas porter de tool calls — cause racine documentée dans
/// docs/jarvis-gap-analysis.md §1.2/1.3.
///
/// Chaque implémentation normalise son format de transport vers <see cref="LLMTurn"/> :
/// les endpoints ne sont PAS interchangeables (le shim OpenAI-compatible d'Ollama perd les
/// tool calls en streaming, l'endpoint natif non).
/// </summary>
public interface ILLMAgentClient
{
    LLMProvider Provider { get; }

    /// <summary>Modèle effectivement utilisé — pour le logging et l'affichage UI.</summary>
    string ModelId { get; }

    /// <summary>
    /// Exécute UN tour de LLM. Les tokens sont poussés au fil de l'eau via <paramref name="onToken"/> ;
    /// les appels d'outils éventuels sont renvoyés dans le résultat.
    /// </summary>
    Task<LLMTurn> StreamTurnAsync(LLMRequest request, Func<string, Task> onToken, CancellationToken ct = default);

    /// <summary>
    /// Vérifie que le modèle répond RÉELLEMENT, en l'appelant.
    /// Ne jamais se contenter de lister les modèles disponibles : un modèle listé peut être
    /// retiré ou verrouillé par abonnement (docs/jarvis-gap-analysis.md §1.10).
    /// </summary>
    Task<bool> ProbeAsync(CancellationToken ct = default);
}
