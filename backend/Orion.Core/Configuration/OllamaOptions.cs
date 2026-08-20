namespace Orion.Core.Configuration;

public class OllamaOptions
{
    public const string SectionName = "Ollama";
    
    public string BaseUrl { get; set; } = "http://localhost:11434";
    public string Model { get; set; } = "deepseek-v4-flash:cloud";
    public string FallbackModel { get; set; } = "llama3.2:3b";
    public int TimeoutSeconds { get; set; } = 120;

    /// <summary>
    /// Fenêtre de contexte demandée à Ollama.
    /// OBLIGATOIRE : sans cette valeur, Ollama dimensionne le cache KV sur le contexte MAXIMUM
    /// du modèle (128k pour llama3.2) et réclame ~15 Go pour un modèle de 2 Go — l'allocation
    /// échoue alors en HTTP 500 dès que la RAM libre baisse. La panne devient intermittente et
    /// dépend de ce qui tourne à côté. Mesuré le 2026-08-20.
    /// </summary>
    public int NumCtx { get; set; } = 8192;

    /// <summary>
    /// Durée pendant laquelle Ollama garde le modèle chargé après le dernier appel.
    /// Par défaut Ollama décharge au bout de 5 min — et le déchargement vide aussi le cache de
    /// préfixe du prompt. La requête suivante repaie alors le chargement ET l'évaluation à froid
    /// du prompt système : 242 s mesurées le 2026-08-20 sur `llama3.2:3b` en CPU, contre 0,4 s
    /// à chaud. Compromis : le modèle occupe la RAM pendant toute cette durée.
    /// </summary>
    public string KeepAlive { get; set; } = "30m";
}
