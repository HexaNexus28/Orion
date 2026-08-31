using System.Diagnostics;
using Microsoft.Extensions.Logging;
using Orion.Daemon.Core.Interfaces;

namespace Orion.Daemon.Watchers;

/// <summary>
/// Surveille CPU et RAM. Emet `high_cpu` / `high_ram` avec leur SEVERITE mesuree.
///
/// La severite est la cle que lit ProactiveDecider. Sans elle le score reste fige a l'urgence de
/// base — 60 pour le CPU, au-dessus du seuil d'interruption de 55 — et tout depassement
/// interrompt, y compris un build parfaitement normal.
/// </summary>
public class SystemWatcher : IWatcher
{
    private readonly ILogger _logger;
    private readonly Timer _checkTimer;
    private bool _isRunning;
    private PerformanceCounter? _cpuCounter;
    private PerformanceCounter? _ramCounter;

    private const double CPU_WARNING_THRESHOLD = 90.0;
    private const double RAM_WARNING_THRESHOLD = 85.0;

    /// <summary>
    /// Echantillons consecutifs au-dessus du seuil avant d'emettre. A 30 s l'echantillon, c'est
    /// 90 s de charge SOUTENUE : une pointe ne ressemble plus a une machine en difficulte.
    /// </summary>
    private const int SUSTAINED_SAMPLES = 3;

    private int _cpuStreak;
    private int _ramStreak;

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

            // Un compteur de TAUX a besoin de deux echantillons : le premier NextValue() rend
            // toujours 0. On le consomme ici pour que la premiere vraie mesure compte.
            _cpuCounter.NextValue();
            _ramCounter.NextValue();
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "[SystemWatcher] Could not initialize performance counters");
        }
    }

    public void Start()
    {
        _isRunning = true;
        _checkTimer.Change(TimeSpan.FromSeconds(30), TimeSpan.FromSeconds(30));
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

    /// <summary>
    /// Position dans la plage « seuil -> 100 % », ramenee sur 0-100. A 90,1 % de CPU la severite
    /// vaut 1 et le signal part au briefing ; a 100 % elle vaut 100 et il interrompt.
    /// </summary>
    private static int Severity(double usage, double threshold)
        => (int)Math.Round(Math.Clamp((usage - threshold) / (100.0 - threshold) * 100.0, 0, 100));

    private void CheckSystem(object? state)
    {
        try
        {
            if (_cpuCounter != null)
            {
                var cpuUsage = _cpuCounter.NextValue();
                _cpuStreak = cpuUsage > CPU_WARNING_THRESHOLD ? _cpuStreak + 1 : 0;

                // `==` et non `>=` : on emet UNE fois quand la charge devient soutenue, pas a
                // chaque echantillon suivant. Le cooldown du decideur gere la repetition.
                if (_cpuStreak == SUSTAINED_SAMPLES)
                {
                    _logger.LogWarning("[SystemWatcher] High CPU usage: {CpuUsage:F1}%", cpuUsage);
                    PatternDetected?.Invoke(this, new PatternDetectedEventArgs
                    {
                        Pattern = "high_cpu",
                        Context = $"CPU a {cpuUsage:F1}% depuis {SUSTAINED_SAMPLES * 30} s",
                        Metadata = new Dictionary<string, object>
                        {
                            ["cpu_percent"] = cpuUsage,
                            ["severity"] = Severity(cpuUsage, CPU_WARNING_THRESHOLD),
                        }
                    });
                }
            }

            if (_ramCounter != null)
            {
                var ramUsage = _ramCounter.NextValue();
                _ramStreak = ramUsage > RAM_WARNING_THRESHOLD ? _ramStreak + 1 : 0;

                if (_ramStreak == SUSTAINED_SAMPLES)
                {
                    _logger.LogWarning("[SystemWatcher] High RAM usage: {RamUsage:F1}%", ramUsage);
                    PatternDetected?.Invoke(this, new PatternDetectedEventArgs
                    {
                        Pattern = "high_ram",
                        Context = $"RAM a {ramUsage:F1}% depuis {SUSTAINED_SAMPLES * 30} s",
                        Metadata = new Dictionary<string, object>
                        {
                            ["ram_percent"] = ramUsage,
                            ["severity"] = Severity(ramUsage, RAM_WARNING_THRESHOLD),
                        }
                    });
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[SystemWatcher] Error checking system");
        }
    }
}
