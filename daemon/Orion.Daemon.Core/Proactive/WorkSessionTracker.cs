using Orion.Daemon.Core.Configuration;

namespace Orion.Daemon.Core.Proactive;

/// <summary>
/// Duree de travail CONTINUE, celle qui survit aux changements d'application.
///
/// <see cref="ActivityContext"/> mesure le temps passe sur l'application COURANTE : il repart a
/// zero des qu'on passe de l'editeur au terminal. C'est ce qu'il faut pour juger la
/// concentration, jamais pour juger le surmenage — passer du code au terminal, c'est toujours
/// travailler.
///
/// La session ne se termine donc pas sur un changement d'application mais sur une vraie PAUSE :
/// un temps continu sans aucune application de travail au premier plan.
///
/// Logique pure, horloge passee en parametre : testable sans Windows.
/// </summary>
public class WorkSessionTracker
{
    private readonly ProactiveOptions _options;
    private readonly object _gate = new();

    private DateTime? _sessionStart;
    private DateTime? _nonWorkSince;
    private DateTime _lastTick;
    private DateTime? _lastSignal;

    public WorkSessionTracker(ProactiveOptions options) => _options = options;

    /// <summary>Ferme la session et rend le droit de signaler.</summary>
    private void Close()
    {
        _sessionStart = null;
        _nonWorkSince = null;
        _lastSignal = null;
    }

    /// <summary>Duree de la session en cours, ou zero si aucune n'est ouverte.</summary>
    public TimeSpan Duration(DateTime now)
    {
        lock (_gate)
        {
            return _sessionStart is null ? TimeSpan.Zero : now - _sessionStart.Value;
        }
    }

    /// <summary>
    /// Un battement du watcher. Rend la severite (0-100) quand le seuil de surmenage vient
    /// d'etre franchi, <c>null</c> le reste du temps.
    ///
    /// Une seule emission par session : le seuil se franchit une fois, il ne se refranchit pas
    /// a chaque battement. Le cooldown du decideur est une seconde barriere, pas la premiere.
    /// </summary>
    public int? Tick(string? foregroundApp, DateTime now)
    {
        lock (_gate)
        {
            var pause = TimeSpan.FromMinutes(Math.Max(1, _options.BreakMinutes));

            // TROU DANS LES MESURES — veille du PC, daemon arrete, session verrouillee. On ne
            // sait pas ce qui s'est passe pendant ce temps, et compter ces heures comme du
            // travail continu serait exactement le mensonge qu'on vient de corriger ailleurs.
            if (_lastTick != default && now - _lastTick > pause) { Close(); }
            _lastTick = now;

            if (ActivityContext.IsWorkApp(foregroundApp))
            {
                _sessionStart ??= now;
                _nonWorkSince = null;
            }
            else if (_sessionStart is not null)
            {
                // La pause se mesure sur du temps OBSERVE hors travail, pas sur l'ecart au
                // dernier battement de travail : aller lire une page web n'est pas une pause.
                _nonWorkSince ??= now;
                if (now - _nonWorkSince.Value >= pause) { Close(); return null; }
            }

            if (_sessionStart is null) return null;

            var threshold = TimeSpan.FromHours(Math.Max(1, _options.OverworkAfterHours));
            var elapsed = now - _sessionStart.Value;
            if (elapsed < threshold) return null;

            if (_lastSignal is not null
                && now - _lastSignal.Value < TimeSpan.FromMinutes(Math.Max(5, _options.OverworkReminderMinutes)))
            {
                return null;
            }

            _lastSignal = now;

            // 0 au seuil, 100 trois heures plus tard : trois heures de travail continu et six
            // heures ne meritent pas la meme insistance.
            var over = (elapsed - threshold).TotalHours;
            return (int)Math.Round(Math.Clamp(over / 3.0 * 100.0, 0, 100));
        }
    }
}
