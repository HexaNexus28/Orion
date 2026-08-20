namespace Orion.Daemon.Core.Proactive;

/// <summary>
/// Ce que l'utilisateur est en train de FAIRE — l'entrée qui manquait au score.
///
/// La boucle de décision voyait l'heure et les métriques système. Elle ne savait pas
/// distinguer « il code depuis quarante minutes » de « il navigue ». Elle coupait donc une
/// session de travail pour un rappel de pause : le défaut qui fait qu'on finit par désactiver
/// ce genre d'assistant.
/// </summary>
public interface IActivityContext
{
    /// <summary>Signale l'application au premier plan. Appelé périodiquement par le watcher.</summary>
    void Signaler(string? application, DateTime maintenant);

    ActivityState Etat(DateTime maintenant);
}

/// <summary>
/// <paramref name="Duree"/> est le temps passé SANS INTERRUPTION sur l'application courante.
/// C'est elle qui distingue la concentration du zapping.
/// </summary>
public record ActivityState(string Application, TimeSpan Duree, bool TravailConcentre)
{
    public static readonly ActivityState Inconnu = new("?", TimeSpan.Zero, false);
}
