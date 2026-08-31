using Orion.Core.Configuration;
using Orion.Core.DTOs.Responses;

namespace Orion.Core.Interfaces.Services;

public interface INewsCollector
{
    /// <summary>
    /// Collecte tous les flux configures. Ne leve jamais : un flux en echec est signale dans
    /// <see cref="NewsHarvest.FailedFeeds"/>, il ne fait pas tomber le briefing.
    /// </summary>
    /// <param name="extraFeeds">
    /// Flux composes a l'execution (voir <see cref="INewsQueryPlanner"/>). Ils rejoignent
    /// exactement le meme tuyau que les flux fixes : meme collecte, meme deduplication, meme
    /// plafond. Rien n'entre dans le briefing sans etre passe par une requete reelle.
    /// </param>
    Task<NewsHarvest> CollectAsync(
        IEnumerable<NewsFeed>? extraFeeds = null, CancellationToken ct = default);
}
