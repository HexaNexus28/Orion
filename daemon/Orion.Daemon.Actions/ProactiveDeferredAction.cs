using System.Text.Json;
using Orion.Daemon.Core.Entities;
using Orion.Daemon.Core.Interfaces;
using Orion.Daemon.Core.Proactive;

namespace Orion.Daemon.Actions;

/// <summary>
/// Rend — et vide — la file des signaux différés.
///
/// La boucle de décision met de côté ce qui est vrai mais pas assez urgent pour interrompre.
/// Sans cette action, ces signaux s'accumulaient dans le daemon et n'étaient jamais dits :
/// on avait construit une file d'attente sans sortie.
///
/// Le backend l'appelle au moment du briefing : ce qui ne méritait pas d'interrompre se dit
/// une fois, groupé, au bon moment.
/// </summary>
public class ProactiveDeferredAction : IAction
{
    private readonly IProactiveDecider _decider;

    public ProactiveDeferredAction(IProactiveDecider decider) => _decider = decider;

    public string Name => "proactive_deferred";

    public Task<DaemonResponse> ExecuteAsync(JsonElement payload, string correlationId)
    {
        // Drainer VIDE la file : un signal différé se dit une fois, pas à chaque briefing.
        var differes = _decider.DrainerDifferes();

        var data = new
        {
            count = differes.Count,
            signals = differes.Select(d => new
            {
                pattern = d.Pattern,
                context = d.Contexte,
                score = d.Score,
                detectedAt = d.Detecte
            })
        };

        return Task.FromResult(DaemonResponse.SuccessResponse(correlationId, data));
    }
}
