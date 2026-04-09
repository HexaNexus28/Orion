using System.Text.Json;
using Microsoft.Extensions.Logging;
using Orion.Core.DTOs.Responses;
using Orion.Core.Interfaces.Daemon;
using Orion.Core.DTOs.Requests;

namespace Orion.Business.Services;

/// <summary>
/// Service pour notifier le daemon de lire la réponse ORION à voix haute
/// </summary>
public class VoiceNotificationService
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
            _logger.LogInformation("[VoiceNotification] Requesting synthesis: {Preview}",
                text.Length > 50 ? text[..50] + "..." : text);

            var request = new DaemonActionRequest
            {
                RequestId = Guid.NewGuid().ToString("N"),
                Action = "synthesize",
                Payload = new { text }
            };

            var result = await _daemonClient.SendActionAsync(request, ct);

            if (!result.Success || result.Data == null)
            {
                _logger.LogWarning("[VoiceNotification] Synthesis request failed: {Msg}", result.Message);
                return null;
            }

            // result.Data est DaemonActionResponse dont .Data est object? (JsonElement après désérialisation)
            if (result.Data.Data is JsonElement jsonData &&
                jsonData.TryGetProperty("audioBase64", out var audioEl) &&
                audioEl.ValueKind == JsonValueKind.String)
            {
                var base64 = audioEl.GetString();
                if (!string.IsNullOrEmpty(base64))
                {
                    var bytes = Convert.FromBase64String(base64);
                    _logger.LogInformation("[VoiceNotification] Received {Kb}KB WAV", bytes.Length / 1024);
                    return bytes;
                }
            }

            _logger.LogWarning("[VoiceNotification] No audio data in response (Kokoro unavailable?)");
            return null;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[VoiceNotification] Failed to synthesize audio");
            return null;
        }
    }
}
