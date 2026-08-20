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
    
    // ── Boucle de décision ──────────────────────────────────────────────────────

    /// <summary>
    /// Nombre maximum d'interruptions par heure. C'est le budget d'attention : ce qui sépare
    /// un collègue d'un spammeur de notifications. Au-delà, les signaux non critiques sont
    /// différés au briefing plutôt que perdus.
    /// </summary>
    public int InterruptionsParHeure { get; set; } = 3;

    /// <summary>Sous ce score, un signal est vrai mais n'interrompt pas : il attend le briefing.</summary>
    public int SeuilInterruption { get; set; } = 55;

    /// <summary>
    /// Au-dessus, l'incident passe TOUJOURS — budget ou non. Perdre une alerte de service mort
    /// pour cause de quota serait absurde.
    /// </summary>
    public int SeuilCritique { get; set; } = 75;

    /// <summary>
    /// Repos minimal entre deux annonces du MÊME pattern. Centralisé ici : cette protection ne
    /// vivait que dans SystemWatcher, les quatre autres watchers n'en avaient aucune.
    /// </summary>
    public int CooldownMinutes { get; set; } = 15;

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
