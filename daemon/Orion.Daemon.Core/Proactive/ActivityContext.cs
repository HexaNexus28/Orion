using Orion.Daemon.Core.Configuration;

namespace Orion.Daemon.Core.Proactive;

/// <summary>
/// Suit l'application au premier plan et depuis combien de temps elle l'est.
/// Logique pure, horloge injectée : testable sans Windows.
/// </summary>
public class ActivityContext : IActivityContext
{
    private readonly ProactiveOptions _options;
    private readonly object _verrou = new();

    private string _application = string.Empty;
    private DateTime _depuis;
    private DateTime _dernierSignal;

    public ActivityContext(ProactiveOptions options) => _options = options;

    /// <summary>
    /// Applications où une interruption coûte cher : reprendre le fil d'un raisonnement de
    /// code prend bien plus longtemps que reprendre la lecture d'une page web.
    /// </summary>
    private static readonly HashSet<string> AppsDeTravail = new(StringComparer.OrdinalIgnoreCase)
    {
        "code", "devenv", "rider", "datagrip", "webstorm", "pycharm", "idea",
        "windowsterminal", "pwsh", "powershell", "cmd", "wsl", "ubuntu",
        "photoshop", "illustrator", "figma", "blender",
    };

    public void Signaler(string? application, DateTime maintenant)
    {
        lock (_verrou)
        {
            var nom = (application ?? string.Empty).Trim();
            _dernierSignal = maintenant;

            // Changement d'application : le compteur de concentration repart de zéro.
            if (!string.Equals(nom, _application, StringComparison.OrdinalIgnoreCase))
            {
                _application = nom;
                _depuis = maintenant;
            }
        }
    }

    public ActivityState Etat(DateTime maintenant)
    {
        lock (_verrou)
        {
            if (string.IsNullOrEmpty(_application)) return ActivityState.Inconnu;

            // Signal trop vieux : le watcher est peut-être arrêté. On ne prétend pas savoir.
            // Mieux vaut interrompre à tort que suspendre les alertes sur une donnée périmée.
            var fraicheur = TimeSpan.FromMinutes(Math.Max(2, _options.FraicheurActiviteMinutes));
            if (maintenant - _dernierSignal > fraicheur) return ActivityState.Inconnu;

            var duree = maintenant - _depuis;
            var concentre = AppsDeTravail.Contains(_application)
                            && duree >= TimeSpan.FromMinutes(Math.Max(1, _options.ConcentrationApresMinutes));

            return new ActivityState(_application, duree, concentre);
        }
    }
}
