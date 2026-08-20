namespace Orion.Core.DTOs.Internal.LLM;

/// <summary>
/// Un appel d'outil demandé par le LLM, normalisé — indépendant du transport
/// (Ollama natif, OpenAI-compatible, ...). Les adaptateurs convertissent vers ce type.
/// </summary>
public class LLMToolCall
{
    /// <summary>Identifiant de corrélation renvoyé au LLM avec le résultat. Généré si absent.</summary>
    public string Id { get; set; } = string.Empty;

    public string Name { get; set; } = string.Empty;

    /// <summary>Arguments bruts en JSON. Toujours un objet JSON valide ("{}" si vide).</summary>
    public string ArgumentsJson { get; set; } = "{}";
}
