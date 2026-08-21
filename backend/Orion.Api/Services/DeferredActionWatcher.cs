using Microsoft.Extensions.Options;
using Orion.Api.Controllers;
using Orion.Core.Configuration;
using Orion.Core.DTOs.Responses;
using Orion.Core.Interfaces.Daemon;
using Orion.Core.Interfaces.Services;

namespace Orion.Api.Services;

/// <summary>
/// Fait le lien entre « le PC vient de se rallumer » et « vide la file ».
///
/// Deux déclencheurs, et ils ne servent pas à la même chose :
///   • l'ÉVÉNEMENT de reconnexion draine immédiatement — c'est le chemin utile ;
///   • le BALAYAGE périodique ne fait qu'expirer, pour qu'une action puisse mourir de
///     vieillesse même si le PC ne revient jamais.
///
/// Le service métier ignore tout du canal SSE : il rend un rapport, cette classe le raconte.
/// </summary>
public class DeferredActionWatcher : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly IDaemonClient _daemon;
    private readonly DaemonOptions _options;
    private readonly ILogger<DeferredActionWatcher> _logger;

    public DeferredActionWatcher(
        IServiceScopeFactory scopeFactory,
        IDaemonClient daemon,
        IOptions<DaemonOptions> options,
        ILogger<DeferredActionWatcher> logger)
    {
        _scopeFactory = scopeFactory;
        _daemon = daemon;
        _options = options.Value;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _daemon.DaemonConnected += SurReconnexion;
        _logger.LogInformation("[DeferredActionWatcher] En écoute — draine dès que le PC revient");

        var periode = TimeSpan.FromMinutes(Math.Max(1, _options.DeferredSweepMinutes));

        try
        {
            while (!stoppingToken.IsCancellationRequested)
            {
                await Task.Delay(periode, stoppingToken);
                await BalayerAsync(stoppingToken);
            }
        }
        catch (OperationCanceledException)
        {
            // Arrêt normal du backend.
        }
        finally
        {
            _daemon.DaemonConnected -= SurReconnexion;
        }
    }

    /// <summary>
    /// L'événement est synchrone et levé depuis le chemin de connexion du daemon : on ne
    /// bloque pas dessus. Le drain enverra ses ordres sur un socket déjà ouvert mais dont la
    /// boucle de réception démarre juste après — les réponses sont mises en tampon par le
    /// WebSocket, et les 30 s de délai d'attente des outils laissent une marge confortable.
    /// </summary>
    private void SurReconnexion(string machineName)
    {
        _ = Task.Run(async () =>
        {
            try
            {
                await DrainerAsync(machineName, CancellationToken.None);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "[DeferredActionWatcher] Drain en échec après reconnexion de {Machine}", machineName);
            }
        });
    }

    private async Task DrainerAsync(string machineName, CancellationToken ct)
    {
        using var scope = _scopeFactory.CreateScope();
        var file = scope.ServiceProvider.GetRequiredService<IDeferredActionService>();

        var resultat = await file.DrainAsync(ct);
        if (!resultat.Success || resultat.Data is null)
        {
            _logger.LogWarning("[DeferredActionWatcher] Drain impossible : {Message}", resultat.Message);
            return;
        }

        var rapport = resultat.Data;
        if (rapport.RienAFaire)
        {
            _logger.LogInformation("[DeferredActionWatcher] {Machine} de retour — rien en attente", machineName);
            return;
        }

        await ProactiveNotificationController.BroadcastAsync(
            eventType: "deferred",
            message: Raconter(rapport),
            priority: rapport.AwaitingConfirmation.Count > 0 ? "high" : "normal",
            speak: true,
            _logger);
    }

    private async Task BalayerAsync(CancellationToken ct)
    {
        try
        {
            using var scope = _scopeFactory.CreateScope();
            var file = scope.ServiceProvider.GetRequiredService<IDeferredActionService>();
            await file.ExpireStaleAsync(ct);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[DeferredActionWatcher] Balayage d'expiration en échec");
        }
    }

    /// <summary>
    /// Le rapport en une phrase. Dire d'abord ce qui ATTEND l'utilisateur, ensuite ce qui est
    /// déjà fait : le premier lui demande quelque chose, le second est une information.
    /// </summary>
    private static string Raconter(DeferredDrainReport rapport)
    {
        var morceaux = new List<string>();

        if (rapport.AwaitingConfirmation.Count > 0)
        {
            var noms = string.Join(", ", rapport.AwaitingConfirmation.Select(a => a.ToolName));
            morceaux.Add($"J'attends ton feu vert pour {noms} — tu me l'avais demandé pendant que ton PC était éteint.");
        }

        if (rapport.Executed.Count > 0)
        {
            var noms = string.Join(", ", rapport.Executed.Select(a => a.ToolName));
            morceaux.Add($"C'est fait : {noms}.");
        }

        if (rapport.Failed.Count > 0)
        {
            var noms = string.Join(", ", rapport.Failed.Select(a => a.ToolName));
            morceaux.Add($"Échec au réveil : {noms}.");
        }

        if (rapport.Expired > 0)
        {
            morceaux.Add($"{rapport.Expired} action(s) avaient trop attendu, je les ai laissées tomber.");
        }

        return string.Join(" ", morceaux);
    }
}
