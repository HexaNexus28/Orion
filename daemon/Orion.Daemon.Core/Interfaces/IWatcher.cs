namespace Orion.Daemon.Core.Interfaces;

/// <summary>
/// Watcher pour surveillance autonome (activité, temps, process, système)
/// </summary>
public interface IWatcher
{
    string Name { get; }
    bool IsRunning { get; }
    
    /// <summary>
    /// Démarrer la surveillance
    /// </summary>
    void Start();
    
    /// <summary>
    /// Arrêter la surveillance
    /// </summary>
    void Stop();
    
    /// <summary>
    /// Événement déclenché quand un pattern est détecté
    /// </summary>
    event EventHandler<PatternDetectedEventArgs>? PatternDetected;
}

public class PatternDetectedEventArgs : EventArgs
{
    public string Pattern { get; set; } = "";
    public string Context { get; set; } = "";
    public DateTime Timestamp { get; set; } = DateTime.UtcNow;
    public Dictionary<string, object> Metadata { get; set; } = new();
}
