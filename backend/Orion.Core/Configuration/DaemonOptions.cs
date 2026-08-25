namespace Orion.Core.Configuration;

public class DaemonOptions
{
    public const string SectionName = "Daemon";
    
    public string WsUrl { get; set; } = "ws://localhost:5001/ws";
    public string WsToken { get; set; } = string.Empty;

    /// <summary>
    /// Durée de vie d'une action différée. Passé ce délai elle expire sans s'exécuter :
    /// un `git_commit` demandé hier soir et rejoué trois jours plus tard n'est pas un service,
    /// c'est une surprise.
    /// </summary>
    public int DeferredTtlHours { get; set; } = 24;

    /// <summary>
    /// Fréquence du balayage d'expiration. Indépendante du réveil du PC : une action doit
    /// pouvoir mourir de vieillesse même si le daemon ne revient jamais.
    /// </summary>
    public int DeferredSweepMinutes { get; set; } = 30;
}
