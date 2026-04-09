using System.Diagnostics;
using System.Runtime.InteropServices;
using Microsoft.Extensions.Logging;
using Orion.Daemon.Core.Configuration;
using Orion.Daemon.Core.Interfaces;

namespace Orion.Daemon.Watchers;

/// <summary>
/// ActivityWatcher - Surveille l'inactivité clavier/souris
/// Déclenche ORION après 3h d'inactivité + pattern skip_meal
/// </summary>
public class ActivityWatcher : IWatcher
{
    private readonly ProactiveOptions _options;
    private readonly ILogger _logger;
    private readonly Timer _checkTimer;
    private DateTime _lastActivity;
    private bool _isRunning;

    public string Name => "ActivityWatcher";
    public bool IsRunning => _isRunning;

    public event EventHandler<PatternDetectedEventArgs>? PatternDetected;

    // Windows API pour l'inactivité
    [DllImport("user32.dll")]
    static extern bool GetLastInputInfo(ref LASTINPUTINFO plii);

    [StructLayout(LayoutKind.Sequential)]
    struct LASTINPUTINFO
    {
        public uint cbSize;
        public uint dwTime;
    }

    public ActivityWatcher(ProactiveOptions options, ILogger logger)
    {
        _options = options;
        _logger = logger;
        _lastActivity = DateTime.UtcNow;
        _checkTimer = new Timer(CheckActivity, null, Timeout.Infinite, Timeout.Infinite);
    }

    public void Start()
    {
        _isRunning = true;
        _checkTimer.Change(TimeSpan.Zero, TimeSpan.FromMinutes(1)); // Check toutes les minutes
        _logger.LogInformation("[ActivityWatcher] Started");
    }

    public void Stop()
    {
        _isRunning = false;
        _checkTimer.Change(Timeout.Infinite, Timeout.Infinite);
        _logger.LogInformation("[ActivityWatcher] Stopped");
    }

    private void CheckActivity(object? state)
    {
        try
        {
            var idleTime = GetIdleTime();
            var now = DateTime.Now;

            // Pattern: skip_meal (inactif depuis 3h + heure repas passée)
            if (idleTime.TotalHours >= 3 && 
                now.TimeOfDay > _options.LunchTime.Add(TimeSpan.FromHours(1)) &&
                _options.EnableMealReminders)
            {
                _logger.LogInformation("[ActivityWatcher] Pattern detected: skip_meal");
                PatternDetected?.Invoke(this, new PatternDetectedEventArgs
                {
                    Pattern = "skip_meal",
                    Context = $"Inactif depuis {idleTime.TotalHours:F1}h, heure repas: {_options.LunchTime}",
                    Metadata = new Dictionary<string, object>
                    {
                        ["idle_hours"] = idleTime.TotalHours,
                        ["current_time"] = now.ToString("HH:mm")
                    }
                });
            }

            // Pattern: overwork (inactif depuis 6h)
            if (idleTime.TotalHours >= 6 && _options.EnableBreakReminders)
            {
                _logger.LogInformation("[ActivityWatcher] Pattern detected: overwork");
                PatternDetected?.Invoke(this, new PatternDetectedEventArgs
                {
                    Pattern = "overwork",
                    Context = $"Inactif depuis {idleTime.TotalHours:F1}h - temps de pause",
                    Metadata = new Dictionary<string, object> { ["idle_hours"] = idleTime.TotalHours }
                });
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[ActivityWatcher] Error checking activity");
        }
    }

    private TimeSpan GetIdleTime()
    {
        try
        {
            var lii = new LASTINPUTINFO { cbSize = (uint)Marshal.SizeOf(typeof(LASTINPUTINFO)) };
            GetLastInputInfo(ref lii);
            var idleTicks = Environment.TickCount - lii.dwTime;
            return TimeSpan.FromMilliseconds(idleTicks);
        }
        catch
        {
            return TimeSpan.Zero;
        }
    }
}
