using Microsoft.Extensions.Logging;
using Orion.Core.DTOs.Requests;
using Orion.Core.DTOs.Responses;
using Orion.Core.Interfaces.Daemon;
using Orion.Core.Interfaces.Services;

namespace Orion.Business.Services;

/// <summary>
/// Service pour notifier le daemon de lire la réponse ORION à voix haute
/// </summary>
public class VoiceNotificationService : IVoiceNotificationService
{
    private readonly IDaemonClient _daemonClient;
    private readonly ILogger<VoiceNotificationService> _logger;

    public VoiceNotificationService(IDaemonClient daemonClient, ILogger<VoiceNotificationService> logger)
    {
        _daemonClient = daemonClient;
        _logger = logger;
    }

    public async Task SpeakAsync(string text, CancellationToken ct = default)
    {
        if (!_daemonClient.IsConnected)
        {
            _logger.LogWarning("[VoiceNotification] Daemon not connected, skipping TTS");
            return;
        }

        try
        {
            _logger.LogInformation("[VoiceNotification] Sending TTS request to daemon: {Text}", 
                text.Length > 50 ? text[..50] + "..." : text);

            var request = new DaemonActionRequest
            {
                RequestId = Guid.NewGuid().ToString("N"),
                Action = "speak",
                Payload = new { text }
            };

            var result = await _daemonClient.SendActionAsync(request, ct);
            
            if (!result.Success)
            {
                _logger.LogWarning("[VoiceNotification] TTS failed: {Error}", result.Message);
            }
            else
            {
                _logger.LogInformation("[VoiceNotification] TTS queued successfully");
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[VoiceNotification] Failed to send TTS request");
        }
    }

    /// <summary>
    /// Synthétise le texte via Kokoro sur le daemon et retourne les bytes WAV.
    /// Le frontend joue l'audio via AudioContext.
    /// Retourne null si le daemon est déconnecté ou Kokoro indisponible.
    /// </summary>
    public async Task<byte[]?> SynthesizeAsync(string text, CancellationToken ct = default)
    {
        if (!_daemonClient.IsConnected)
        {
            _logger.LogWarning("[VoiceNotification] Daemon not connected, cannot synthesize");
            return null;
        }

        try
        {
            _logger.LogDebug("[VoiceNotification] Requesting synthesis: {Preview}",
                text.Length > 50 ? text[..50] + "..." : text);

            var request = new DaemonActionRequest
            {
                RequestId = Guid.NewGuid().ToString("N"),
                Action = "synthesize",
                Payload = new { text }
            };

            using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            cts.CancelAfter(TimeSpan.FromSeconds(10));

            // Binary WS protocol — daemon sends raw WAV bytes, no base64 overhead
            var result = await _daemonClient.SendBinaryActionAsync(request, cts.Token);

            if (!result.Success || result.Data == null || result.Data.Length == 0)
            {
                _logger.LogWarning("[VoiceNotification] Synthesis failed: {Msg}", result.Message);
                return null;
            }

            _logger.LogDebug("[VoiceNotification] Received {Kb}KB WAV binary", result.Data.Length / 1024);
            return result.Data;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[VoiceNotification] Failed to synthesize audio");
            return null;
        }
    }
}
