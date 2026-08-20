namespace Orion.Daemon.Core.Proactive;

/// <summary>Ce qu'ORION fait d'un signal détecté.</summary>
public enum ProactiveAction
{
    /// <summary>Interrompre maintenant : ça vaut l'attention de l'utilisateur.</summary>
    Parler,

    /// <summary>Vrai mais pas urgent : mis de côté pour le briefing du lendemain.</summary>
    Differer,

    /// <summary>Répétition, bruit, ou budget d'attention épuisé.</summary>
    Taire
}

/// <summary>
/// La décision, et surtout sa RAISON. Un assistant qui se tait sans dire pourquoi est
/// indiscernable d'un assistant en panne — c'est le silence qui a coûté des mois à ce projet.
/// </summary>
public record ProactiveDecision(ProactiveAction Action, int Score, string Raison)
{
    public static ProactiveDecision Parler(int score, string raison) => new(ProactiveAction.Parler, score, raison);
    public static ProactiveDecision Differer(int score, string raison) => new(ProactiveAction.Differer, score, raison);
    public static ProactiveDecision Taire(int score, string raison) => new(ProactiveAction.Taire, score, raison);
}

/// <summary>Un signal mis de côté, en attente du prochain briefing.</summary>
public record SignalDiffere(string Pattern, string Contexte, int Score, DateTime Detecte);
