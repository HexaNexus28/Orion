using System.Diagnostics;
using Microsoft.Extensions.Logging;
using Orion.Daemon.Core.Interfaces;

namespace Orion.Daemon.Watchers;

/// <summary>
/// ProcessWatcher - Détecte les applications ouvertes (VS Code, browser...)
/// </summary>
public class ProcessWatcher : IWatcher
{
    private readonly ILogger _logger;
    private readonly Timer _checkTimer;
    private bool _isRunning;
    private readonly HashSet<string> _interestingProcesses = new(StringComparer.OrdinalIgnoreCase)
    {
        "code",          // VS Code
        "chrome",
        "firefox",
        "msedge",
        "devenv",        // Visual Studio
        "rider",
        "datagrip"
    };

    public string Name => "ProcessWatcher";
    public bool IsRunning => _isRunning;

    public event EventHandler<PatternDetectedEventArgs>? PatternDetected;

    public ProcessWatcher(ILogger logger)
    {
        _logger = logger;
        _checkTimer = new Timer(CheckProcesses, null, Timeout.Infinite, Timeout.Infinite);
    }

    public void Start()
    {
        _isRunning = true;
        _checkTimer.Change(TimeSpan.FromMinutes(5), TimeSpan.FromMinutes(5)); // Check toutes les 5min
        _logger.LogInformation("[ProcessWatcher] Started");
    }

    public void Stop()
    {
        _isRunning = false;
        _checkTimer.Change(Timeout.Infinite, Timeout.Infinite);
        _logger.LogInformation("[ProcessWatcher] Stopped");
    }

    private void CheckProcesses(object? state)
    {
        try
        {
            var activeApps = new List<string>();
            var processes = Process.GetProcesses();

            foreach (var process in processes)
            {
                try
                {
                    if (_interestingProcesses.Contains(process.ProcessName) && !string.IsNullOrEmpty(process.MainWindowTitle))
                    {
                        activeApps.Add($"{process.ProcessName}: {process.MainWindowTitle}");
                    }
                }
                catch { /* ignore errors accessing process */ }
            }

            if (activeApps.Any())
            {
                _logger.LogDebug("[ProcessWatcher] Active apps: {Apps}", string.Join(", ", activeApps.Take(5)));
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[ProcessWatcher] Error checking processes");
        }
    }

    /// <summary>
    /// Retourne les applications actuellement actives
    /// </summary>
    public List<string> GetActiveApplications()
    {
        var result = new List<string>();
        try
        {
            var processes = Process.GetProcesses();
            foreach (var process in processes)
            {
                try
                {
                    if (_interestingProcesses.Contains(process.ProcessName) && !string.IsNullOrEmpty(process.MainWindowTitle))
                    {
                        result.Add(process.ProcessName);
                    }
                }
                catch { }
            }
        }
        catch { }
        return result.Distinct().ToList();
    }
}
