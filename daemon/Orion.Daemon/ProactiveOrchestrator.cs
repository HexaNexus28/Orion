using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using Orion.Daemon.Core.Configuration;
using Orion.Daemon.Core.Interfaces;
using Orion.Daemon.Core.Proactive;
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
    private readonly IProactiveDecider _decider;
    private Timer? _apprentissageTimer;
    private readonly ILogger _logger;
    private readonly HttpClient _httpClient;
    private readonly string _backendHttpUrl;

    public ProactiveOrchestrator(
        IEnumerable<IWatcher> watchers,
        IEnumerable<INotifier> notifiers,
        DaemonWebSocketManager wsManager,
        ProactiveOptions options,
        DaemonOptions daemonOptions,
        IProactiveDecider decider,
        ILogger logger)
    {
        _decider = decider;
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

        // Rafraichit les penalites apprises. Sans ce rappel periodique, un « ne me dis plus ca »
        // ne prendrait effet qu'au prochain redemarrage du daemon.
        _apprentissageTimer = new Timer(async _ => await RafraichirPenalitesAsync(), null,
            TimeSpan.FromSeconds(20), TimeSpan.FromMinutes(15));

        _logger.LogInformation("[ProactiveOrchestrator] All watchers started");
    }

    /// <summary>
    /// Va chercher au backend ce qu'ORION a appris des refus de l'utilisateur.
    /// Backend injoignable : on garde les dernieres penalites connues plutot que de repartir
    /// a zero — reoublier ce que l'utilisateur a deja refuse serait le pire des comportements.
    /// </summary>
    private async Task RafraichirPenalitesAsync()
    {
        try
        {
            var reponse = await _httpClient.GetAsync($"{_backendHttpUrl}/api/proactivenotification/weights");
            if (!reponse.IsSuccessStatusCode) return;

            var body = await reponse.Content.ReadAsStringAsync();
            using var doc = JsonDocument.Parse(body);

            if (!doc.RootElement.TryGetProperty("data", out var data) || data.ValueKind != JsonValueKind.Object)
                return;

            var penalites = data.EnumerateObject()
                .Where(p => p.Value.ValueKind == JsonValueKind.Number)
                .ToDictionary(p => p.Name, p => p.Value.GetInt32(), StringComparer.OrdinalIgnoreCase);

            _decider.AppliquerPenalites(penalites);

            if (penalites.Count > 0)
            {
                _logger.LogInformation("[Apprentissage] {N} signal(aux) attenue(s) : {Detail}",
                    penalites.Count, string.Join(", ", penalites.Select(p => $"{p.Key}-{p.Value}")));
            }
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "[Apprentissage] Penalites non rafraichies");
        }
    }

    public void Stop()
    {
        _apprentissageTimer?.Dispose();
        _apprentissageTimer = null;

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
            // ── LA boucle de décision ────────────────────────────────────────────
            // Avant, tout pattern détecté devenait une parole, immédiatement et sans
            // condition. ORION n'avait aucun moyen de se taire.
            var maintenant = DateTime.UtcNow;
            var decision = _decider.Decider(e, maintenant);
            _decider.Enregistrer(e, decision, maintenant);

            _logger.LogInformation(
                "[Proactif] {Pattern} ({Watcher}) — score {Score} → {Action} : {Raison}",
                e.Pattern, sender?.GetType().Name ?? "?", decision.Score, decision.Action, decision.Raison);

            if (decision.Action != ProactiveAction.Parler)
                return; // différé au briefing, ou tu — dans les deux cas on n'interrompt pas

            // Le backend rédige le message et le diffuse en SSE. S'il est injoignable,
            // on retombe sur un message local.
            var (message, clientsPrevenus) = await GenerateProactiveMessage(e);

            if (string.IsNullOrEmpty(message)) return;

            // DEUX VOIX : le front prononce déjà le message reçu en SSE. Parler AUSSI en local
            // faisait entendre la même phrase deux fois — le commentaire disait « fallback si
            // le navigateur est fermé », mais rien ne vérifiait qu'il l'était.
            // Le backend renvoie déjà le nombre de clients SSE prévenus : il suffisait de le lire.
            if (clientsPrevenus > 0)
            {
                _logger.LogDebug("[Proactif] {N} client(s) SSE parlent deja — pas de TTS local", clientsPrevenus);
                return;
            }

            await NotifyAll(message, e.Pattern);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[ProactiveOrchestrator] Error handling pattern");
        }
    }

    /// <summary>
    /// Renvoie le message ET le nombre de clients SSE déjà prévenus — c'est ce chiffre qui
    /// décide si le daemon doit parler à son tour ou se taire.
    /// </summary>
    private async Task<(string Message, int ClientsPrevenus)> GenerateProactiveMessage(PatternDetectedEventArgs pattern)
    {
        var (backendMessage, clients) = await TriggerLLMMessageAsync(pattern.Pattern, pattern.Context);
        if (!string.IsNullOrEmpty(backendMessage))
            return (backendMessage, clients);

        // Backend injoignable : personne n'a été prévenu, donc le daemon parle.
        return (GetFallbackMessage(pattern.Pattern, pattern.Context), 0);
    }

    private async Task<(string Message, int ClientsPrevenus)> TriggerLLMMessageAsync(string pattern, string context)
    {
        try
        {
            var payload = new { pattern, context, priority = "normal" };
            var json = JsonSerializer.Serialize(payload);
            var content = new StringContent(json, Encoding.UTF8, "application/json");
            var url = $"{_backendHttpUrl}/api/proactivenotification/trigger";

            var response = await _httpClient.PostAsync(url, content);
            if (!response.IsSuccessStatusCode)
            {
                _logger.LogWarning("[ProactiveOrchestrator] Trigger returned {Status}", response.StatusCode);
                return (string.Empty, 0);
            }

            var body = await response.Content.ReadAsStringAsync();
            using var doc = System.Text.Json.JsonDocument.Parse(body);
            var data = doc.RootElement.GetProperty("data");

            var message = data.GetProperty("message").GetString() ?? string.Empty;
            var clients = data.TryGetProperty("clientsNotified", out var c) ? c.GetInt32() : 0;

            _logger.LogInformation("[ProactiveOrchestrator] LLM message ({Clients} client(s) SSE): {Preview}",
                clients, message.Length > 60 ? message[..60] + "..." : message);

            return (message, clients);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[ProactiveOrchestrator] Backend trigger failed");
            return (string.Empty, 0);
        }
    }

    private static string GetFallbackMessage(string pattern, string context) => pattern switch
    {
        "skip_meal"  => "T'as mangé ? Tu es inactif depuis plusieurs heures.",
        "overwork"   => "Tu travailles depuis longtemps. Prends une pause.",
        "meal_time"  => "C'est l'heure de manger.",
        "break_time" => "Pause méritée.",
        "night_time" => "Il se fait tard. Pense à dormir.",
        "high_cpu"   => "Ton CPU est surchargé. Ferme ce que tu n'utilises pas.",
        "high_ram"   => "RAM presque pleine. Redémarre ou ferme des apps.",
        _            => context
    };

    /// <summary>
    /// Vide la file des signaux différés. Destiné au briefing : ce qui n'était pas assez
    /// urgent pour interrompre reste vrai, et se dit une fois, groupé.
    /// </summary>
    public IReadOnlyList<SignalDiffere> RecupererDifferes() => _decider.DrainerDifferes();

    private async Task NotifyAll(string message, string pattern = "proactive")
    {
        // Le /trigger a déjà broadcasté via SSE si le backend est joignable.
        // Ici on gère uniquement le fallback TTS local (browser fermé).
        var ttsNotifier = _notifiers.FirstOrDefault(n => n.Name == "PowerShellTtsNotifier" && n.IsAvailable);
        if (ttsNotifier != null)
        {
            try
            {
                await ttsNotifier.SpeakAsync(message);
                _logger.LogInformation("[ProactiveOrchestrator] Fallback TTS local: {Preview}",
                    message.Length > 50 ? message[..50] + "..." : message);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "[ProactiveOrchestrator] Fallback TTS échoué");
            }
        }
    }
}
