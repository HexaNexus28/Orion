namespace Orion.Daemon.Core.Configuration;

/// <summary>
/// Configuration pour le mode proactif ORION
/// </summary>
public class ProactiveOptions
{
    /// <summary>
    /// Inactivité avant notification (minutes)
    /// </summary>
    public int InactivityThresholdMinutes { get; set; } = 180; // 3h
    
    /// <summary>
    /// Activer les notifications repas
    /// </summary>
    public bool EnableMealReminders { get; set; } = true;
    public TimeSpan LunchTime { get; set; } = new TimeSpan(13, 0, 0);
    
    /// <summary>
    /// Activer les notifications pause
    /// </summary>
    public bool EnableBreakReminders { get; set; } = true;
    public TimeSpan BreakTime { get; set; } = new TimeSpan(17, 0, 0);
    
    /// <summary>
    /// Activer les notifications nuit
    /// </summary>
    public bool EnableNightReminders { get; set; } = true;
    public TimeSpan NightTime { get; set; } = new TimeSpan(23, 0, 0);
    
    /// <summary>
    /// Patterns utilisateur à surveiller
    /// </summary>
    public List<string> MonitoredPatterns { get; set; } = new()
    {
        "skip_meal",
        "overwork",
        "late_night"
    };
}
