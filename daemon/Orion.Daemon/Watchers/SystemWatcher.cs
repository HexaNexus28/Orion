using System.Diagnostics;
using Microsoft.Extensions.Logging;
using Orion.Daemon.Core.Interfaces;

namespace Orion.Daemon.Watchers;

/// <summary>
/// SystemWatcher - Surveille CPU, RAM, réseau - alertes si anomalie
/// </summary>
public class SystemWatcher : IWatcher
{
    private readonly ILogger _logger;
    private readonly Timer _checkTimer;
    private bool _isRunning;
    private PerformanceCounter? _cpuCounter;
    private PerformanceCounter? _ramCounter;

    // Seuils d'alerte
    private const double CPU_WARNING_THRESHOLD = 90.0;  // 90%
    private const double RAM_WARNING_THRESHOLD = 85.0;  // 85%
    
    // Cooldown anti-spam (minutes entre 2 alertes identiques)
    private static readonly TimeSpan COOLDOWN = TimeSpan.FromMinutes(15);
    private readonly Dictionary<string, DateTime> _lastTriggered = new();

    public string Name => "SystemWatcher";
    public bool IsRunning => _isRunning;

    public event EventHandler<PatternDetectedEventArgs>? PatternDetected;

    public SystemWatcher(ILogger logger)
    {
        _logger = logger;
        _checkTimer = new Timer(CheckSystem, null, Timeout.Infinite, Timeout.Infinite);
        
        try
        {
            _cpuCounter = new PerformanceCounter("Processor", "% Processor Time", "_Total");
            _ramCounter = new PerformanceCounter("Memory", "% Committed Bytes In Use");
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "[SystemWatcher] Could not initialize performance counters");
        }
    }

    public void Start()
    {
        _isRunning = true;
        _checkTimer.Change(TimeSpan.FromSeconds(30), TimeSpan.FromSeconds(30)); // Check toutes les 30s
        _logger.LogInformation("[SystemWatcher] Started");
    }

    public void Stop()
    {
        _isRunning = false;
        _checkTimer.Change(Timeout.Infinite, Timeout.Infinite);
        _cpuCounter?.Dispose();
        _ramCounter?.Dispose();
        _logger.LogInformation("[SystemWatcher] Stopped");
    }

    private bool CanTrigger(string pattern)
    {
        if (_lastTriggered.TryGetValue(pattern, out var lastTime))
        {
            return DateTime.UtcNow - lastTime >= COOLDOWN;
        }
        return true;
    }

    private void RecordTrigger(string pattern)
    {
        _lastTriggered[pattern] = DateTime.UtcNow;
    }

    private void CheckSystem(object? state)
    {
        try
        {
            // Check CPU
            if (_cpuCounter != null)
            {
                var cpuUsage = _cpuCounter.NextValue();
                if (cpuUsage > CPU_WARNING_THRESHOLD && CanTrigger("high_cpu"))
                {
                    _logger.LogWarning("[SystemWatcher] High CPU usage: {CpuUsage:F1}%", cpuUsage);
                    PatternDetected?.Invoke(this, new PatternDetectedEventArgs
                    {
                        Pattern = "high_cpu",
                        Context = $"CPU à {cpuUsage:F1}% - ralentissement possible",
                        Metadata = new Dictionary<string, object> { ["cpu_percent"] = cpuUsage }
                    });
                    RecordTrigger("high_cpu");
                }
            }

            // Check RAM
            if (_ramCounter != null)
            {
                var ramUsage = _ramCounter.NextValue();
                if (ramUsage > RAM_WARNING_THRESHOLD && CanTrigger("high_ram"))
                {
                    _logger.LogWarning("[SystemWatcher] High RAM usage: {RamUsage:F1}%", ramUsage);
                    PatternDetected?.Invoke(this, new PatternDetectedEventArgs
                    {
                        Pattern = "high_ram",
                        Context = $"RAM à {ramUsage:F1}% - fermer des applications?",
                        Metadata = new Dictionary<string, object> { ["ram_percent"] = ramUsage }
                    });
                    RecordTrigger("high_ram");
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[SystemWatcher] Error checking system");
        }
    }
}
