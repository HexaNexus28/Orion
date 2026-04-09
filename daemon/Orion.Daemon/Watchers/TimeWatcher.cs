using Microsoft.Extensions.Logging;
using Orion.Daemon.Core.Configuration;
using Orion.Daemon.Core.Interfaces;

namespace Orion.Daemon.Watchers;

/// <summary>
/// TimeWatcher - Crons locaux (repas 13h, pause 17h, nuit 23h)
/// </summary>
public class TimeWatcher : IWatcher
{
    private readonly ProactiveOptions _options;
    private readonly ILogger _logger;
    private readonly Timer _checkTimer;
    private bool _isRunning;
    private readonly HashSet<string> _triggeredToday = new();

    public string Name => "TimeWatcher";
    public bool IsRunning => _isRunning;

    public event EventHandler<PatternDetectedEventArgs>? PatternDetected;

    public TimeWatcher(ProactiveOptions options, ILogger logger)
    {
        _options = options;
        _logger = logger;
        _checkTimer = new Timer(CheckTime, null, Timeout.Infinite, Timeout.Infinite);
    }

    public void Start()
    {
        _isRunning = true;
        _checkTimer.Change(TimeSpan.Zero, TimeSpan.FromMinutes(1)); // Check toutes les minutes
        _logger.LogInformation("[TimeWatcher] Started");
    }

    public void Stop()
    {
        _isRunning = false;
        _checkTimer.Change(Timeout.Infinite, Timeout.Infinite);
        _logger.LogInformation("[TimeWatcher] Stopped");
    }

    private void CheckTime(object? state)
    {
        try
        {
            var now = DateTime.Now;
            var timeKey = now.ToString("yyyy-MM-dd");

            // Reset daily at midnight
            if (now.TimeOfDay < TimeSpan.FromMinutes(1))
            {
                _triggeredToday.Clear();
            }

            // Repas midi
            if (_options.EnableMealReminders && 
                now.TimeOfDay >= _options.LunchTime &&
                now.TimeOfDay < _options.LunchTime.Add(TimeSpan.FromMinutes(5)) &&
                !_triggeredToday.Contains($"lunch_{timeKey}"))
            {
                _logger.LogInformation("[TimeWatcher] Lunch time triggered");
                _triggeredToday.Add($"lunch_{timeKey}");
                PatternDetected?.Invoke(this, new PatternDetectedEventArgs
                {
                    Pattern = "meal_time",
                    Context = "Il est l'heure du déjeuner",
                    Metadata = new Dictionary<string, object> { ["time"] = "lunch" }
                });
            }

            // Pause après-midi
            if (_options.EnableBreakReminders && 
                now.TimeOfDay >= _options.BreakTime &&
                now.TimeOfDay < _options.BreakTime.Add(TimeSpan.FromMinutes(5)) &&
                !_triggeredToday.Contains($"break_{timeKey}"))
            {
                _logger.LogInformation("[TimeWatcher] Break time triggered");
                _triggeredToday.Add($"break_{timeKey}");
                PatternDetected?.Invoke(this, new PatternDetectedEventArgs
                {
                    Pattern = "break_time",
                    Context = "Il est l'heure de faire une pause",
                    Metadata = new Dictionary<string, object> { ["time"] = "break" }
                });
            }

            // Nuit
            if (_options.EnableNightReminders && 
                now.TimeOfDay >= _options.NightTime &&
                now.TimeOfDay < _options.NightTime.Add(TimeSpan.FromMinutes(5)) &&
                !_triggeredToday.Contains($"night_{timeKey}"))
            {
                _logger.LogInformation("[TimeWatcher] Night time triggered");
                _triggeredToday.Add($"night_{timeKey}");
                PatternDetected?.Invoke(this, new PatternDetectedEventArgs
                {
                    Pattern = "night_time",
                    Context = "Il est tard - temps de se coucher",
                    Metadata = new Dictionary<string, object> { ["time"] = "night" }
                });
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[TimeWatcher] Error checking time");
        }
    }
}
