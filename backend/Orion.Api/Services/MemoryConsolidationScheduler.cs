using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Orion.Core.Interfaces.Services;

namespace Orion.Api.Services;

/// <summary>
/// Distille périodiquement les épisodes bruts en faits durables.
///
/// POURQUOI CE FICHIER EXISTE. `MemoryConsolidator` était enregistré dans le conteneur mais
/// AUCUN service d'arrière-plan ne le déclenchait : il ne tournait que si le modèle appelait
/// de lui-même l'outil `memory_reflect`. Autant dire jamais. Les épisodes s'accumulaient donc
/// sans jamais devenir des souvenirs, et la mémoire durable restait vide — même une fois
/// l'écriture automatique des épisodes en place.
///
/// Écrire les épisodes sans jamais les consolider ne suffirait pas : la recherche remonterait
/// des bouts de conversation bruts au lieu de faits, et le schéma fermé à 4 slots — la garde
/// qui distingue une mémoire d'un dépotoir — ne serait jamais appliqué.
///
/// CADENCE. Une passe consomme au plus 30 épisodes et appelle le modèle une fois. Toutes les
/// six heures suffit largement pour un usage personnel, et laisse la matière se former entre
/// deux passes : distiller trop tôt produit des faits tirés d'un seul échange, donc des
/// anecdotes promues en règles.
///
/// Le consolidateur rend la main immédiatement quand il n'y a aucun épisode : un réveil à vide
/// ne coûte rien, et surtout il n'appelle pas le modèle.
/// </summary>
public class MemoryConsolidationScheduler : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<MemoryConsolidationScheduler> _logger;

    private static readonly TimeSpan Intervalle = TimeSpan.FromHours(6);

    /// <summary>
    /// On ne consolide pas au démarrage. Le service peut redémarrer souvent — déploiement,
    /// plantage, mise à l'échelle — et chaque redémarrage déclencherait alors un appel au
    /// modèle sur une matière inchangée.
    /// </summary>
    private static readonly TimeSpan DelaiInitial = TimeSpan.FromMinutes(10);

    public MemoryConsolidationScheduler(
        IServiceScopeFactory scopeFactory,
        ILogger<MemoryConsolidationScheduler> logger)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation(
            "[Consolidation] Planificateur demarre — premiere passe dans {Delai}, puis toutes les {Intervalle}",
            DelaiInitial, Intervalle);

        try
        {
            await Task.Delay(DelaiInitial, stoppingToken);

            while (!stoppingToken.IsCancellationRequested)
            {
                await ConsoliderAsync(stoppingToken);
                await Task.Delay(Intervalle, stoppingToken);
            }
        }
        catch (OperationCanceledException)
        {
            // Arrêt normal de l'application.
        }
    }

    private async Task ConsoliderAsync(CancellationToken ct)
    {
        try
        {
            using var scope = _scopeFactory.CreateScope();
            var consolidateur = scope.ServiceProvider.GetRequiredService<IMemoryConsolidator>();

            var resultat = await consolidateur.ConsolidateAsync(ct);

            if (!resultat.Success || resultat.Data is null)
            {
                _logger.LogWarning("[Consolidation] Passe en echec : {Msg}", resultat.Message);
                return;
            }

            var rapport = resultat.Data;

            // Rien à distiller est le cas NORMAL entre deux conversations : on le journalise en
            // Debug pour ne pas noyer les journaux d'un message toutes les six heures.
            if (rapport.EpisodesExamines == 0)
            {
                _logger.LogDebug("[Consolidation] Aucun episode a distiller");
                return;
            }

            _logger.LogInformation(
                "[Consolidation] {Examines} episode(s) examine(s) -> {Ecrits} souvenir(s), "
                + "{Consommes} consomme(s), {Perimes} etat(s) perime(s) supprime(s)",
                rapport.EpisodesExamines, rapport.SouvenirsEcrits,
                rapport.EpisodesConsommes, rapport.EtatsPerimesSupprimes);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            // Une passe ratée ne doit pas tuer le planificateur : les épisodes ne sont pas
            // consommés en cas d'échec, la prochaine passe les relira.
            _logger.LogError(ex, "[Consolidation] Passe interrompue par une exception");
        }
    }
}
