using Orion.Core.DTOs.Responses;

namespace Orion.Core.Interfaces.Services;

/// <summary>
/// Nature du texte vectorisé. Les modèles de recherche NVIDIA sont ASYMÉTRIQUES : ils projettent
/// une question et un document dans le même espace mais par des chemins différents. Vectoriser une
/// recherche comme un document dégrade la pertinence SANS lever d'erreur — exactement le genre de
/// panne muette qui a déjà coûté des mois ici. Le type est donc obligatoire, jamais deviné.
/// </summary>
public enum EmbeddingInputType
{
    /// <summary>Contenu que l'on STOCKE et qui sera retrouvé plus tard.</summary>
    Passage,

    /// <summary>Texte de RECHERCHE, comparé aux passages déjà stockés.</summary>
    Query
}

public interface IEmbeddingService
{
    /// <summary>
    /// Modele reellement utilise. Ecrit A COTE de chaque vecteur : c'est ce qui rend un melange
    /// d'espaces vectoriels DETECTABLE au lieu d'empoisonner la recherche en silence.
    /// </summary>
    string ModelName { get; }

    /// <summary>
    /// Vectorise un texte. <paramref name="inputType"/> doit refleter l usage REEL :
    /// Passage a l ecriture, Query a la lecture. Se tromper degrade la pertinence sans erreur.
    /// </summary>
    Task<ApiResponse<float[]>> GenerateEmbeddingAsync(
        string text,
        EmbeddingInputType inputType,
        CancellationToken ct = default);
}
