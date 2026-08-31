using Orion.Core.Configuration;

namespace Orion.Core.Interfaces.Services;

/// <summary>
/// Compose des flux de veille a l'execution, a partir de ce qu'ORION sait de l'utilisateur.
///
/// Le modele n'y produit que des MOTS-CLES. Ils deviennent des URL de flux reellement
/// interrogees : ce qui entre dans le briefing vient toujours d'une source, jamais des poids
/// du modele.
/// </summary>
public interface INewsQueryPlanner
{
    /// <summary>Ne leve jamais : sans requete dynamique, la veille retombe sur les flux fixes.</summary>
    Task<IReadOnlyList<NewsFeed>> PlanAsync(CancellationToken ct = default);
}
