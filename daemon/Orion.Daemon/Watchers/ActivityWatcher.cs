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

            // ACTIF, pas inactif. La version precedente exigeait 3 h d'inactivite passe l'heure
            // du repas : elle decrivait quelqu'un en train de manger, et lui reprochait de ne
            // pas manger. On saute un repas quand on est ENCORE devant la machine.
            if (idleTime < TimeSpan.FromMinutes(15) &&
                now.TimeOfDay > _options.LunchTime.Add(TimeSpan.FromHours(1)) &&
                _options.EnableMealReminders)
            {
                _logger.LogInformation("[ActivityWatcher] Pattern detected: skip_meal");
                PatternDetected?.Invoke(this, new PatternDetectedEventArgs
                {
                    Pattern = "skip_meal",
                    Context = $"Toujours au clavier, il est {now:HH:mm} et l'heure du repas ({_options.LunchTime:hh\\hmm}) est passee",
                    Metadata = new Dictionary<string, object>
                    {
                        ["idle_minutes"] = Math.Round(idleTime.TotalMinutes, 1),
                        ["current_time"] = now.ToString("HH:mm"),
                        ["severity"] = 30
                    }
                });
            }

            // `overwork` a ete RETIRE d'ici : il se declenchait sur six heures d'INACTIVITE,
            // c'est-a-dire sur quelqu'un qui n'avait pas touche la machine — et lui annoncait
            // « temps de pause ». Le message le disait lui-meme : « Inactif depuis 6,0h - temps
            // de pause ». Le surmenage se mesure sur du travail CONTINU, ce que seul
            // ProcessWatcher sait faire (voir WorkSessionTracker).
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
