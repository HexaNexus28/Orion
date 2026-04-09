namespace Orion.Daemon.Core.Interfaces;

/// <summary>
/// Notifier pour canaux de sortie (Windows notifications, TTS)
/// </summary>
public interface INotifier
{
    string Name { get; }
    bool IsAvailable { get; }
    
    /// <summary>
    /// Envoyer une notification
    /// </summary>
    Task NotifyAsync(string title, string message, NotificationPriority priority = NotificationPriority.Normal);
    
    /// <summary>
    /// Synthèse vocale (si supporté)
    /// </summary>
    Task SpeakAsync(string text);
}

public enum NotificationPriority
{
    Low,
    Normal,
    High,
    Critical
}
