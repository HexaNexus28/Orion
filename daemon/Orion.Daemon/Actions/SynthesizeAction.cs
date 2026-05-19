using System.Text.Json;
using Orion.Daemon.Core.Entities;
using Orion.Daemon.Core.Interfaces;
using Orion.Daemon.Notifiers;

namespace Orion.Daemon.Actions;

/// <summary>
/// Synthétise le texte en WAV et retourne les bytes en base64 au backend.
/// Le backend les retransmet au frontend qui joue via AudioContext.
/// Contrairement à SpeakAction (qui joue localement), SynthesizeAction
/// est utilisé pour les réponses chat destinées au frontend.
/// </summary>
public class SynthesizeAction : IAction
{
    public string Name => "synthesize";

    private readonly KokoroSpeaker _speaker;
    private readonly ILogger _logger;

    public SynthesizeAction(KokoroSpeaker speaker, ILogger logger)
    {
        _speaker = speaker;
        _logger = logger;
    }

    public async Task<DaemonResponse> ExecuteAsync(JsonElement payload, string correlationId)
    {
        try
        {
            var text = payload.GetProperty("text").GetString();
            if (string.IsNullOrEmpty(text))
                return DaemonResponse.ErrorResponse(correlationId, "Text is required");

            if (!_speaker.IsAvailable)
            {
                _logger.LogWarning("[SynthesizeAction] Kokoro indisponible");
                return DaemonResponse.SuccessResponse(correlationId, new { audioBase64 = (string?)null, available = false });
            }

            var wavBytes = await _speaker.SynthesizeToWavAsync(text);
            if (wavBytes == null || wavBytes.Length == 0)
            {
                _logger.LogWarning("[SynthesizeAction] Synthesis returned empty audio");
                return DaemonResponse.SuccessResponse(correlationId, new { available = false });
            }

            _logger.LogInformation("[SynthesizeAction] Returning {Kb}KB WAV binary to backend", wavBytes.Length / 1024);

            // Return BinaryPayload — DaemonWebSocketManager sends as binary WS frame
            // Protocol: [36-byte correlationId] + [raw WAV bytes] — no base64 overhead
            return DaemonResponse.SuccessResponse(correlationId, new BinaryPayload(wavBytes));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[SynthesizeAction] Failed");
            return DaemonResponse.ErrorResponse(correlationId, ex.Message);
        }
    }
}
