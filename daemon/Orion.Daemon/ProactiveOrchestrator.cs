using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using Orion.Daemon.Core.Configuration;
using Orion.Daemon.Core.Interfaces;
using Orion.Daemon.WebSocket;

namespace Orion.Daemon;

/// <summary>
/// ProactiveOrchestrator - Connecte Watchers → LLM → Notifiers
/// 
/// Flux:
/// 14h23 — ActivityWatcher : inactif depuis 3h + pattern skip_meal détecté
///       → POST backend /trigger/proactive { context, time, pattern }
///       → LLM génère : "T'as mangé ?"
///       → WindowsNotifier : notification Windows
///       → SapiSpeaker : ORION dit ça à voix haute
///       → Tout ça sans que tu ouvres quoi que ce soit
/// </summary>
public class ProactiveOrchestrator
{
    private readonly IEnumerable<IWatcher> _watchers;
    private readonly IEnumerable<INotifier> _notifiers;
    private readonly DaemonWebSocketManager _wsManager;
    private readonly ProactiveOptions _options;
    private readonly ILogger _logger;
    private readonly HttpClient _httpClient;
    private readonly string _backendHttpUrl;

    public ProactiveOrchestrator(
        IEnumerable<IWatcher> watchers,
        IEnumerable<INotifier> notifiers,
        DaemonWebSocketManager wsManager,
        ProactiveOptions options,
        DaemonOptions daemonOptions,
        ILogger logger)
    {
        _watchers = watchers;
        _notifiers = notifiers;
        _wsManager = wsManager;
        _options = options;
        _logger = logger;
        _httpClient = new HttpClient();
        _httpClient.DefaultRequestHeaders.Add("X-Daemon-Token", daemonOptions.Token);

        // Dériver l'URL HTTP depuis l'URL WebSocket
        // ws://localhost:5107/daemon  → http://localhost:5107
        // wss://orion-api.onrender.com/daemon → https://orion-api.onrender.com
        _backendHttpUrl = daemonOptions.RenderWsUrl
            .Replace("wss://", "https://")
            .Replace("ws://", "http://")
            .Replace("/daemon", "");
    }

    public void Start()
    {
        _logger.LogInformation("[ProactiveOrchestrator] Starting...");

        // Subscribe to all watcher events
        foreach (var watcher in _watchers)
        {
            watcher.PatternDetected += OnPatternDetected;
            watcher.Start();
            _logger.LogInformation("[ProactiveOrchestrator] Started watcher: {WatcherName}", watcher.Name);
        }

        _logger.LogInformation("[ProactiveOrchestrator] All watchers started");
    }

    public void Stop()
    {
        foreach (var watcher in _watchers)
        {
            watcher.PatternDetected -= OnPatternDetected;
            watcher.Stop();
        }
        _logger.LogInformation("[ProactiveOrchestrator] Stopped");
    }

    private async void OnPatternDetected(object? sender, PatternDetectedEventArgs e)
    {
        try
        {
            _logger.LogInformation(
                "[ProactiveOrchestrator] Pattern detected: {Pattern} from {Watcher}",
                e.Pattern, sender?.GetType().Name ?? "Unknown");

            // 1. Envoyer au backend pour génération LLM
            var message = await GenerateProactiveMessage(e);
            
            if (!string.IsNullOrEmpty(message))
            {
                // 2. Notifier via tous les canaux disponibles
                await NotifyAll(message);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[ProactiveOrchestrator] Error handling pattern");
        }
    }

    private async Task<string> GenerateProactiveMessage(PatternDetectedEventArgs pattern)
    {
        try
        {
            // Pour l'instant, messages prédéfinis
            // À terme: appel backend pour génération LLM personnalisée
            var message = pattern.Pattern switch
            {
                "skip_meal" => "T'as mangé ? Tu es inactif depuis plusieurs heures et c'est l'heure du repas.",
                "overwork" => "Tu travailles depuis longtemps. Tu devrais faire une pause.",
                "meal_time" => "Il est l'heure du déjeuner !",
                "break_time" => "C'est l'heure de la pause. Tu as bien mérité un moment de repos.",
                "night_time" => "Il se fait tard. Pense à aller te coucher pour être en forme demain.",
                "high_cpu" => "Ton CPU est surchargé. Je te conseille de fermer quelques applications.",
                "high_ram" => "Ta mémoire RAM est presque pleine. Tu devrais redémarrer ou fermer des programmes.",
                _ => pattern.Context
            };

            _logger.LogInformation("[ProactiveOrchestrator] Generated message: {Message}", 
                message.Length > 50 ? message[..50] + "..." : message);

            return await Task.FromResult(message);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[ProactiveOrchestrator] Failed to generate message");
            return "ORION a détecté quelque chose mais n'a pas pu formuler de message.";
        }
    }

    private async Task NotifyAll(string message, string pattern = "proactive")
    {
        // 1. Envoyer au backend → SSE → frontend → Web Speech API (chemin principal)
        //    Le frontend parle via Web Speech API si le browser est ouvert
        var backendReached = await SendToBackendAsync(message, pattern);

        // 2. Fallback TTS local si le frontend n'est pas joignable (browser fermé)
        //    PowerShell SAPI parle directement — PAS de popup Windows
        if (!backendReached)
        {
            var ttsNotifier = _notifiers.FirstOrDefault(n => n.Name == "PowerShellTtsNotifier" && n.IsAvailable);
            if (ttsNotifier != null)
            {
                try
                {
                    await ttsNotifier.SpeakAsync(message);
                    _logger.LogInformation("[ProactiveOrchestrator] Fallback TTS (PowerShell) utilisé");
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "[ProactiveOrchestrator] Fallback TTS échoué");
                }
            }
        }
        // Aucun Toast/popup Windows — ORION parle, il n'affiche pas
    }

    private async Task<bool> SendToBackendAsync(string message, string pattern)
    {
        try
        {
            var payload = new
            {
                type = "proactive",
                title = "ORION",
                message,
                priority = "normal",
                speak = true,
                timestamp = DateTime.UtcNow
            };

            var json = JsonSerializer.Serialize(payload);
            var content = new StringContent(json, Encoding.UTF8, "application/json");
            var url = $"{_backendHttpUrl}/api/proactivenotification/notify";

            var response = await _httpClient.PostAsync(url, content);

            if (response.IsSuccessStatusCode)
            {
                _logger.LogInformation("[ProactiveOrchestrator] SSE → frontend: {Preview}",
                    message.Length > 50 ? message[..50] + "..." : message);
                return true;
            }

            _logger.LogWarning("[ProactiveOrchestrator] Backend returned {Status}", response.StatusCode);
            return false;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[ProactiveOrchestrator] Backend injoignable — fallback TTS local");
            return false;
        }
    }
}
