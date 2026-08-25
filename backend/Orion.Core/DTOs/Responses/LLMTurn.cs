using Orion.Core.DTOs.Internal.LLM;

namespace Orion.Core.DTOs.Responses;

/// <summary>
/// Résultat d'UN tour de LLM : le texte généré et les appels d'outils demandés.
/// Un tour avec des ToolCalls n'est pas une réponse finale — la boucle doit exécuter
/// les outils et rappeler le modèle.
/// </summary>
public class LLMTurn
{
    public string Content { get; set; } = string.Empty;

    public List<LLMToolCall> ToolCalls { get; set; } = new();

    public string Model { get; set; } = string.Empty;

    public int? TokensUsed { get; set; }

    public bool HasToolCalls => ToolCalls.Count > 0;
}
