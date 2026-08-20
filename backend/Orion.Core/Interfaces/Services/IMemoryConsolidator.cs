using Orion.Core.DTOs.Responses;

namespace Orion.Core.Interfaces.Services;

/// <summary>
/// La passe de réflexion : relit les épisodes bruts et en distille des faits durables,
/// rangés dans le schéma fermé <see cref="Orion.Core.Enums.MemorySlot"/>.
///
/// Sans elle, la mémoire n'accumule que des échanges : elle grossit, ralentit la recherche,
/// et sa valeur par souvenir baisse. C'est l'étage qui transforme un historique en savoir.
/// </summary>
public interface IMemoryConsolidator
{
    Task<ApiResponse<ConsolidationReport>> ConsolidateAsync(CancellationToken ct = default);
}
