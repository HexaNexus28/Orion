using Microsoft.Extensions.Logging;
using Orion.Core.DTOs.Requests;
using Orion.Core.DTOs.Responses;
using Orion.Core.Enums;
using Orion.Core.Interfaces.LLM;

namespace Orion.Business.LLM;

/// <summary>
/// Cascade de fournisseurs LLM. C'est le VRAI routeur — l'ancien `LLMRouter` n'en était pas un :
/// un seul client codé en dur, et un `.Result` bloquant à chaque requête.
///
/// Les clients sont essayés dans l'ordre d'enregistrement (distant d'abord, local en dernier).
/// La sonde de démarrage élit le premier qui répond RÉELLEMENT, et la bascule est criée dans les
/// logs — jamais silencieuse. C'est ce silence qui a fait tourner ORION des mois sur un modèle
/// dégradé sans que personne ne le sache (docs/jarvis-gap-analysis.md §1.11).
/// </summary>
public class LLMCascade : ILLMAgentClient
{
    private readonly IReadOnlyList<ILLMAgentClient> _clients;
    private readonly ILogger<LLMCascade> _logger;

    private ILLMAgentClient? _active;

    public LLMCascade(IEnumerable<ILLMAgentClient> clients, ILogger<LLMCascade> logger)
    {
        _clients = clients.ToList();
        _logger = logger;

        if (_clients.Count == 0)
            throw new InvalidOperationException("Aucun client LLM enregistre dans la cascade.");
    }

    public LLMProvider Provider => _active?.Provider ?? LLMProvider.None;

    public string ModelId => _active?.ModelId ?? "aucun";

    /// <summary>Élit le premier fournisseur qui répond vraiment.</summary>
    public async Task<bool> ProbeAsync(CancellationToken ct = default)
    {
        foreach (var client in _clients)
        {
            if (await client.ProbeAsync(ct))
            {
                _active = client;

                if (!ReferenceEquals(client, _clients[0]))
                {
                    _logger.LogError(
                        "[Cascade] FOURNISSEUR PRINCIPAL INDISPONIBLE — repli sur {Provider} ({Model}). "
                        + "Capacites potentiellement reduites.",
                        client.Provider, client.ModelId);
                }
                else
                {
                    _logger.LogInformation("[Cascade] Fournisseur actif : {Provider} ({Model})",
                        client.Provider, client.ModelId);
                }

                return true;
            }

            _logger.LogWarning("[Cascade] {Provider} indisponible, essai du suivant", client.Provider);
        }

        _active = null;
        return false;
    }

    public Task<LLMTurn> StreamTurnAsync(
        LLMRequest request,
        Func<string, Task> onToken,
        CancellationToken ct = default)
    {
        var client = _active
            ?? throw new InvalidOperationException(
                "Aucun fournisseur LLM operationnel. La sonde de demarrage a echoue sur tous.");

        return client.StreamTurnAsync(request, onToken, ct);
    }
}
