using System.Text.Json;
using Microsoft.Extensions.Logging;
using Orion.Daemon.Core.Interfaces;

namespace Orion.Daemon.Watchers;

/// <summary>
/// AdaptiveWatcher - Apprend des habitudes utilisateur et auto-ajuste les paramètres
/// 
/// Auto-progression:
/// - Détecte les patterns récurrents (heures de travail, pauses, apps)
/// - Ajuste les thresholds CPU/RAM selon l'usage réel
/// - Propose des optimisations sans demander
/// - Apprend des feedbacks (notification ignorée = moins intrusive)
/// </summary>
public class AdaptiveWatcher : IWatcher
{
    private readonly ILogger _logger;
    private readonly Timer _learningTimer;
    private readonly string _profilePath;
    private bool _isRunning;

    // Profil utilisateur auto-généré
    private UserProfile _profile = new();

    public string Name => "AdaptiveWatcher";
    public bool IsRunning => _isRunning;
    public event EventHandler<PatternDetectedEventArgs>? PatternDetected;

    public AdaptiveWatcher(ILogger logger)
    {
        _logger = logger;
        _learningTimer = new Timer(Learn, null, Timeout.Infinite, Timeout.Infinite);
        _profilePath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "Orion",
            "adaptive-profile.json");
        
        LoadProfile();
    }

    public void Start()
    {
        _isRunning = true;
        // Analyse toutes les 5 minutes
        _learningTimer.Change(TimeSpan.FromMinutes(5), TimeSpan.FromMinutes(5));
        _logger.LogInformation("[AdaptiveWatcher] Started - Profile: {Profile}", _profile.ToString());
    }

    public void Stop()
    {
        _isRunning = false;
        _learningTimer.Change(Timeout.Infinite, Timeout.Infinite);
        SaveProfile();
        _logger.LogInformation("[AdaptiveWatcher] Stopped");
    }

    /// <summary>
    /// Méthode principale d'apprentissage - appelée automatiquement
    /// </summary>
    private void Learn(object? state)
    {
        try
        {
            var now = DateTime.Now;
            var hour = now.Hour;

            // 1. Apprend les heures de travail
            LearnWorkHours(hour);

            // 2. Détecte les patterns d'applications
            LearnAppPatterns();

            // 3. Ajuste les seuils système selon historique
            AdaptSystemThresholds();

            // 4. Proactive: suggère des optimisations
            ProposeOptimizations();

            SaveProfile();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[AdaptiveWatcher] Learning error");
        }
    }

    private void LearnWorkHours(int hour)
    {
        // Détecte automatiquement les créneaux de travail
        var active = IsUserActive();
        
        if (active && !_profile.WorkingHours.Contains(hour))
        {
            _profile.WorkingHours.Add(hour);
            _logger.LogDebug("[AdaptiveWatcher] Learned work hour: {Hour}h", hour);
        }

        // Détecte le début/fin de journée
        if (active && hour >= 6 && hour <= 10 && !_profile.DayStartDetected)
        {
            _profile.TypicalDayStart = hour;
            _profile.DayStartDetected = true;
            _logger.LogInformation("[AdaptiveWatcher] Detected day start: ~{Hour}h", hour);
        }
    }

    private void LearnAppPatterns()
    {
        // Analyse les processus pour détecter les workflows
        try
        {
            var processes = System.Diagnostics.Process.GetProcesses()
                .Where(p => !string.IsNullOrEmpty(p.ProcessName))
                .Select(p => p.ProcessName.ToLower())
                .Distinct()
                .ToList();

            // Détecte les combinaisons (ex: code + browser = dev)
            var devApps = new[] { "code", "chrome", "firefox", "edge" };
            var creativeApps = new[] { "photoshop", "illustrator", "figma" };

            if (devApps.Any(d => processes.Contains(d)) && processes.Contains("code"))
            {
                _profile.CurrentContext = "development";
            }
            else if (creativeApps.Any(c => processes.Contains(c)))
            {
                _profile.CurrentContext = "creative";
            }
            else
            {
                _profile.CurrentContext = "general";
            }
        }
        catch { }
    }

    private void AdaptSystemThresholds()
    {
        // Ajuste les seuils CPU/RAM selon le contexte
        switch (_profile.CurrentContext)
        {
            case "development":
                // Dev: VS Code + Docker + navigateurs = CPU souvent haut
                // Adapter pour ne pas spammer
                if (_profile.CpuSpikes.Count > 10)
                {
                    var avgSpike = _profile.CpuSpikes.Average();
                    _profile.AdaptedCpuThreshold = Math.Min(95, avgSpike + 5);
                    _logger.LogInformation("[AdaptiveWatcher] Adapted CPU threshold to {Threshold}% (dev mode)", 
                        _profile.AdaptedCpuThreshold);
                }
                break;
            
            case "creative":
                // Creative: Accepte des pics plus hauts
                _profile.AdaptedCpuThreshold = 85;
                break;
            
            default:
                _profile.AdaptedCpuThreshold = 90;
                break;
        }
    }

    private void ProposeOptimizations()
    {
        // Détecte les opportunités d'optimisation
        var now = DateTime.Now;
        
        // Ex: Utilisateur ouvre toujours les mêmes apps le matin
        if (now.Hour == _profile.TypicalDayStart && !_profile.MorningRoutineTriggered)
        {
            _profile.MorningRoutineTriggered = true;
            
            PatternDetected?.Invoke(this, new PatternDetectedEventArgs
            {
                Pattern = "adaptive_morning_routine",
                Context = "Je peux ouvrir vos applications habituelles ?",
                Metadata = new Dictionary<string, object>
                {
                    ["usual_apps"] = _profile.FrequentApps.Take(3).ToList(),
                    ["context"] = _profile.CurrentContext,
                    ["suggested_action"] = "open_routine"
                }
            });
        }

        // Reset flags
        if (now.Hour > 12)
        {
            _profile.MorningRoutineTriggered = false;
        }
    }

    /// <summary>
    /// Feedback utilisateur - appelé quand l'utilisateur réagit
    /// </summary>
    public void RecordFeedback(string pattern, bool positive)
    {
        if (!_profile.PatternFeedback.ContainsKey(pattern))
        {
            _profile.PatternFeedback[pattern] = new PatternStats();
        }

        var stats = _profile.PatternFeedback[pattern];
        if (positive)
        {
            stats.SuccessCount++;
            stats.Intrusiveness = Math.Max(0.1, stats.Intrusiveness * 0.9); // Moins intrusif
        }
        else
        {
            stats.IgnoreCount++;
            stats.Intrusiveness = Math.Min(1.0, stats.Intrusiveness * 1.2); // Plus discret
        }

        _logger.LogInformation("[AdaptiveWatcher] Feedback recorded: {Pattern} = {Positive} (intrusiveness: {Intrusiveness:F2})",
            pattern, positive, stats.Intrusiveness);
    }

    private bool IsUserActive()
    {
        // Simplification: considère actif si entre 8h et 20h
        var hour = DateTime.Now.Hour;
        return hour >= 8 && hour <= 20;
    }

    private void LoadProfile()
    {
        try
        {
            if (File.Exists(_profilePath))
            {
                var json = File.ReadAllText(_profilePath);
                _profile = JsonSerializer.Deserialize<UserProfile>(json) ?? new();
                _logger.LogInformation("[AdaptiveWatcher] Profile loaded from {Path}", _profilePath);
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "[AdaptiveWatcher] Failed to load profile");
            _profile = new();
        }
    }

    private void SaveProfile()
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(_profilePath)!);
            var json = JsonSerializer.Serialize(_profile, new JsonSerializerOptions { WriteIndented = true });
            File.WriteAllText(_profilePath, json);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "[AdaptiveWatcher] Failed to save profile");
        }
    }

    /// <summary>
    /// Retourne les insights pour le ProactiveOrchestrator
    /// </summary>
    public UserProfile GetInsights() => _profile;
}

/// <summary>
/// Profil utilisateur auto-généré
/// </summary>
public class UserProfile
{
    public HashSet<int> WorkingHours { get; set; } = new();
    public int TypicalDayStart { get; set; } = 9;
    public bool DayStartDetected { get; set; }
    public bool MorningRoutineTriggered { get; set; }
    public string CurrentContext { get; set; } = "general";
    public double AdaptedCpuThreshold { get; set; } = 90;
    public double AdaptedRamThreshold { get; set; } = 85;
    public List<double> CpuSpikes { get; set; } = new();
    public List<string> FrequentApps { get; set; } = new();
    public Dictionary<string, PatternStats> PatternFeedback { get; set; } = new();

    public override string ToString() => 
        $"Context={CurrentContext}, WorkHours={WorkingHours.Count}h, CPUThreshold={AdaptedCpuThreshold}%";
}

public class PatternStats
{
    public int SuccessCount { get; set; }
    public int IgnoreCount { get; set; }
    public double Intrusiveness { get; set; } = 0.5; // 0-1, plus c'est bas = moins intrusif
}
