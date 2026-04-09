using System.Text.Json;
using Orion.Daemon.Core.Entities;
using Orion.Daemon.Core.Interfaces;
using Orion.Daemon.Notifiers;

namespace Orion.Daemon.Actions;

/// <summary>
/// Action pour faire parler ORION via Kokoro TTS (fallback : PowerShell SAPI)
/// Retourne immédiatement au backend — la synthèse s'effectue en arrière-plan.
/// </summary>
public class SpeakAction : IAction
{
    public string Name => "speak";

    private readonly KokoroSpeaker _speaker;
    private readonly PowerShellTtsNotifier _fallback;
    private readonly ILogger _logger;

    public SpeakAction(KokoroSpeaker speaker, PowerShellTtsNotifier fallback, ILogger logger)
    {
        _speaker = speaker;
        _fallback = fallback;
        _logger = logger;
    }

    public Task<DaemonResponse> ExecuteAsync(JsonElement payload, string correlationId)
    {
        try
        {
            var text = payload.GetProperty("text").GetString();
            if (string.IsNullOrEmpty(text))
                return Task.FromResult(DaemonResponse.ErrorResponse(correlationId, "Text is required"));

            _logger.LogInformation("[SpeakAction] TTS: {Preview}", text[..Math.Min(60, text.Length)]);

            // Fire-and-forget — le backend reçoit la réponse immédiatement
            // La synthèse vocale continue en arrière-plan sur le daemon
            _ = Task.Run(async () =>
            {
                try
                {
                    if (_speaker.IsAvailable)
                    {
                        await _speaker.SpeakAsync(text);
                    }
                    else
                    {
                        _logger.LogInformation("[SpeakAction] Kokoro indisponible → fallback PowerShell SAPI");
                        await _fallback.SpeakAsync(text);
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "[SpeakAction] Kokoro échoué → tentative fallback");
                    try
                    {
                        await _fallback.SpeakAsync(text);
                    }
                    catch (Exception fallbackEx)
                    {
                        _logger.LogError(fallbackEx, "[SpeakAction] Tous les moteurs TTS ont échoué");
                    }
                }
            });

            return Task.FromResult(DaemonResponse.SuccessResponse(correlationId, new { speaking = true }));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[SpeakAction] Erreur inattendue");
            return Task.FromResult(DaemonResponse.ErrorResponse(correlationId, ex.Message));
        }
    }
}
