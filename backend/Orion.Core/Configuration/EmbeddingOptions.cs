namespace Orion.Core.Configuration;

/// <summary>
/// Fournisseur d'embeddings, compatible OpenAI (`POST {BaseUrl}/embeddings`).
///
/// UN SEUL service, N fournisseurs — même principe que le cerveau. Changer de fournisseur est
/// une affaire de CONFIGURATION, jamais de code.
///
/// ⚠️ Contrairement au cerveau, un embedding NE BASCULE PAS à chaud : chaque modèle projette dans
/// son propre espace vectoriel. Changer de fournisseur impose de REVECTORISER toute la table.
/// Mélanger deux espaces ne lève aucune erreur et renvoie des résultats absurdes — c'est pour ça
/// que le modèle et la dimension sont écrits À CÔTÉ de chaque vecteur (colonnes de memory_vectors)
/// et vérifiés au démarrage.
///
/// Mesuré le 2026-08-25, en APPELANT les API (le catalogue ment) :
///   mistral-embed                     VIVANT  1024 dims  ← retenu, indexable (pgvector plafonne à 2000)
///   nvidia/nemotron-3-embed-1b        VIVANT  2048 dims  ← repli, mais NON indexable
///   nvidia/llama-3.2-nv-embedqa-1b-v2 410 Gone
///   nvidia/llama-3.2-nv-embedqa-1b-v1 · embed-qa-4 · arctic-embed-l · nv-embedqa-mistral-7b-v2 → 404
///   gemini text-embedding-004         404 sur l'endpoint compatible OpenAI
/// </summary>
public class EmbeddingOptions
{
    public const string SectionName = "Embedding";

    public string BaseUrl { get; set; } = "https://api.mistral.ai/v1";

    public string ApiKey { get; set; } = string.Empty;

    public string Model { get; set; } = "mistral-embed";

    /// <summary>
    /// Doit correspondre EXACTEMENT à la dimension déclarée de `memory_vectors.embedding`.
    /// Le service refuse un vecteur d'une autre taille plutôt que de laisser la base échouer
    /// plus tard avec un message incompréhensible.
    /// </summary>
    public int Dimensions { get; set; } = 1024;

    /// <summary>
    /// Les modèles de recherche NVIDIA exigent `input_type` (query/passage) et sont ASYMÉTRIQUES.
    /// Mistral ne connaît pas ce paramètre et le REFUSE. D'où ce drapeau : on n'envoie le champ
    /// que si le fournisseur le comprend.
    /// </summary>
    public bool SupportsInputType { get; set; } = false;

    public int TimeoutSeconds { get; set; } = 30;

    public bool IsConfigured => !string.IsNullOrWhiteSpace(ApiKey);
}
