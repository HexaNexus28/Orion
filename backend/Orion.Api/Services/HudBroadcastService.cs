using System.Text.Json.Nodes;
using Orion.Core.Interfaces.Daemon;
using Orion.Core.Interfaces.Tools;

namespace Orion.Api.Services;

/// <summary>
/// Alimente les panneaux PERMANENTS du HUD, independamment de toute conversation.
///
/// POURQUOI CE SERVICE. Une carte produite par un outil n'apparait que si le modele a decide
/// d'appeler cet outil — c'est-a-dire rarement. L'ecran resterait vide la plupart du temps,
/// alors qu'une fenetre de statut doit etre LA en permanence : elle ne s'affiche pas parce qu'on
/// a pose une question.
///
/// Il ne reimplemente RIEN : il rejoue l outil existant et reutilise sa carte
/// (GetSystemStatusTool.BuildCard). Ajouter un panneau permanent revient donc a diffuser la
/// carte d un outil de plus, jamais a ecrire un second producteur de cartes.
/// </summary>
public class HudBroadcastService : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly IDaemonClient _daemon;
    private readonly SseClientRegistry _sse;
    private readonly ILogger<HudBroadcastService> _logger;

    /// <summary>
    /// 60 s. Assez pour que la duree de fonctionnement bouge visiblement, assez peu pour ne pas
    /// solliciter le PC en continu — chaque tour reveille le daemon par WebSocket.
    /// </summary>
    private static readonly TimeSpan Interval = TimeSpan.FromSeconds(60);

    public HudBroadcastService(
        IServiceScopeFactory scopeFactory,
        IDaemonClient daemon,
        SseClientRegistry sse,
        ILogger<HudBroadcastService> logger)
    {
        _scopeFactory = scopeFactory;
        _daemon = daemon;
        _sse = sse;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await BroadcastHostStatusAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                // Un panneau qui ne se rafraichit pas ne doit JAMAIS faire tomber le service :
                // il rendrait aussi muettes les notifications proactives qui partagent ce flux.
                _logger.LogWarning(ex, "[HUD] Diffusion de l etat du poste echouee");
            }

            await Task.Delay(Interval, stoppingToken);
        }
    }

    private async Task BroadcastHostStatusAsync(CancellationToken ct)
    {
        // Deux raisons de ne rien faire, et aucune n est une erreur : PC eteint, ou personne
        // devant l ecran. Interroger le poste pour zero spectateur serait du reveil gratuit.
        if (!_daemon.IsConnected || _sse.Count == 0) return;

        using var scope = _scopeFactory.CreateScope();
        var invoker = scope.ServiceProvider.GetRequiredService<IToolInvoker>();

        // InvokeNowAsync et non InvokeAsync : un sondage d affichage ne doit JAMAIS partir dans
        // la file des actions differees. Une carte en retard de douze heures ne vaut rien, et
        // elle encombrerait une file reservee a ce qui compte vraiment.
        var response = await invoker.InvokeNowAsync("get_system_status", new JsonObject(), ct);

        var card = response.Data?.Card;
        if (card is null) return;

        await _sse.BroadcastAsync("card", card);
    }
}