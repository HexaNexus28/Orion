using Orion.Daemon.Core.Configuration;
using Orion.Daemon.Core.Interfaces;
using Orion.Daemon.Notifiers;
using Orion.Daemon.WebSocket;
using Orion.Daemon.Watchers;

namespace Orion.Daemon;

public class DaemonWorker : BackgroundService
{
    private readonly ILogger<DaemonWorker> _logger;
    private readonly DaemonOptions _options;
    private readonly ProactiveOptions _proactiveOptions;
    private readonly IActionRegistry _actionRegistry;
    private DaemonWebSocketManager? _wsManager;
    private ProactiveOrchestrator? _proactiveOrchestrator;

    public DaemonWorker(
        ILogger<DaemonWorker> logger,
        DaemonOptions options,
        ProactiveOptions proactiveOptions,
        IActionRegistry actionRegistry)
    {
        _logger = logger;
        _options = options;
        _proactiveOptions = proactiveOptions;
        _actionRegistry = actionRegistry;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("[ORION DAEMON] Starting...");
        _logger.LogInformation("[ORION DAEMON] Target: {Url}", _options.RenderWsUrl);
        _logger.LogInformation("[ORION DAEMON] Machine: {Machine}", _options.MachineName);
        _logger.LogInformation("[ORION DAEMON] Registered {Count} actions", _actionRegistry.GetAllActions().Count());

        // Start WebSocket connection
        _wsManager = new DaemonWebSocketManager(_options, _actionRegistry, _logger);

        // Start Proactive Orchestrator
        StartProactiveMode();

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await _wsManager.ConnectAsync(stoppingToken);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Connection failed, retrying...");
                await Task.Delay(_options.ReconnectDelayMs, stoppingToken);
            }
        }

        _proactiveOrchestrator?.Stop();
    }

    private void StartProactiveMode()
    {
        try
        {
            if (_wsManager == null) return;

            // Create watchers
            var watchers = new List<IWatcher>
            {
                new ActivityWatcher(_proactiveOptions, _logger),
                new TimeWatcher(_proactiveOptions, _logger),
                new ProcessWatcher(_logger),
                new SystemWatcher(_logger),
                new AdaptiveWatcher(_logger)  // Auto-learning / self-improving
            };

            // Create notifiers - Toast moderne, TTS PowerShell, Kokoro ONNX
            var notifiers = new List<INotifier>
            {
                new WindowsToastNotifier(_logger),   // Notifications Toast modernes (Win 10/11)
                new WindowsNotifier(_logger),        // Fallback MessageBox (legacy)
                new PowerShellTtsNotifier(_logger),  // TTS via PowerShell/SAPI 5
                new KokoroSpeaker(_logger)           // TTS neuronal (si modèle présent)
            };

            // Start orchestrator
            _proactiveOrchestrator = new ProactiveOrchestrator(
                watchers,
                notifiers,
                _wsManager,
                _proactiveOptions,
                _options,
                _logger);

            _proactiveOrchestrator.Start();

            _logger.LogInformation("[ORION DAEMON] Proactive mode enabled");
            _logger.LogInformation("[ORION DAEMON] Watchers: {Watchers}", 
                string.Join(", ", watchers.Select(w => w.Name)));
            _logger.LogInformation("[ORION DAEMON] Notifiers: {Notifiers}", 
                string.Join(", ", notifiers.Select(n => n.Name)));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[ORION DAEMON] Failed to start proactive mode");
        }
    }
}
