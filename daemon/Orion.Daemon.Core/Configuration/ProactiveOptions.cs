namespace Orion.Daemon.Core.Configuration;

/// <summary>Mode proactif : quand ORION a le droit d'interrompre.</summary>
public class ProactiveOptions
{
    public int InactivityThresholdMinutes { get; set; } = 180;

    public bool EnableMealReminders { get; set; } = true;
    public TimeSpan LunchTime { get; set; } = new TimeSpan(13, 0, 0);

    public bool EnableBreakReminders { get; set; } = true;
    public TimeSpan BreakTime { get; set; } = new TimeSpan(17, 0, 0);

    public bool EnableNightReminders { get; set; } = true;
    public TimeSpan NightTime { get; set; } = new TimeSpan(23, 0, 0);

    // ── Boucle de décision ──────────────────────────────────────────────────────

    /// <summary>Budget d'attention : au-delà, les signaux non critiques attendent le briefing.</summary>
    public int InterruptionsPerHour { get; set; } = 3;

    /// <summary>Sous ce score, un signal est vrai mais n'interrompt pas.</summary>
    public int InterruptionThreshold { get; set; } = 55;

    /// <summary>Au-dessus, l'incident passe TOUJOURS, budget ou non.</summary>
    public int CriticalThreshold { get; set; } = 75;

    /// <summary>Repos minimal entre deux annonces du MÊME pattern, tous watchers confondus.</summary>
    public int CooldownMinutes { get; set; } = 15;

    /// <summary>Durée sur une application de travail au-delà de laquelle on juge l'utilisateur concentré.</summary>
    public int FocusAfterMinutes { get; set; } = 15;

    /// <summary>
    /// Au-delà, le dernier signal d'activité est périmé et on cesse de s'y fier : se taire sur
    /// une donnée morte serait pire qu'interrompre à tort.
    /// </summary>
    public int ActivityFreshnessMinutes { get; set; } = 3;

    /// <summary>Ajouté au seuil pendant une session concentrée. Les incidents critiques y échappent.</summary>
    public int FocusPenalty { get; set; } = 25;

    public List<string> MonitoredPatterns { get; set; } = new()
    {
        "skip_meal",
        "overwork",
        "late_night"
    };
}
