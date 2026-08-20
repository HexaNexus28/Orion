using Orion.Daemon.Core.Interfaces;

namespace Orion.Daemon.Core.Proactive;

/// <summary>
/// La boucle de décision qui manquait. Avant elle, TOUT pattern détecté devenait une parole,
/// immédiatement et sans condition : ORION n'avait aucun moyen de se taire.
///
/// Point d'application UNIQUE — l'anti-répétition vivait dans un seul watcher sur cinq,
/// donc chaque nouveau watcher devait la réécrire ou l'oubliait.
/// </summary>
public interface IProactiveDecider
{
    /// <summary>Décide sans rien modifier. `maintenant` est injecté pour rester testable.</summary>
    ProactiveDecision Decider(PatternDetectedEventArgs signal, DateTime maintenant);

    /// <summary>Enregistre une décision appliquée : c'est ce qui alimente cooldown et budget.</summary>
    void Enregistrer(PatternDetectedEventArgs signal, ProactiveDecision decision, DateTime maintenant);

    /// <summary>Vide la file des signaux différés — appelé au briefing.</summary>
    IReadOnlyList<SignalDiffere> DrainerDifferes();
}
