using System.Diagnostics;
using System.Runtime.InteropServices;
using Microsoft.Extensions.Logging;
using Orion.Daemon.Core.Interfaces;
using Orion.Daemon.Core.Proactive;

namespace Orion.Daemon.Watchers;

/// <summary>
/// Suit l'application au PREMIER PLAN et alimente le contexte d'activité.
///
/// Version précédente : il listait les applications *lancées* toutes les cinq minutes et
/// écrivait un log Debug. Il n'émettait jamais aucun pattern — l'événement `PatternDetected`
/// n'était invoqué nulle part — et « Chrome tourne » ne dit rien de ce que fait l'utilisateur,
/// puisque Chrome tourne toujours.
///
/// Ce qui compte, c'est la fenêtre ACTIVE et depuis combien de temps elle l'est. C'est la seule
/// mesure qui distingue « il code depuis quarante minutes » de « il zappe ».
/// </summary>
public class ProcessWatcher : IWatcher
{
    private readonly IActivityContext _activite;
    private readonly ILogger _logger;
    private readonly Timer _checkTimer;
    private bool _isRunning;

    private string _dernierNom = string.Empty;

    /// <summary>
    /// 30 s : assez fin pour dater un changement d'application sans peser. À cinq minutes,
    /// la durée de concentration était mesurée à cinq minutes près — inutilisable.
    /// </summary>
    private static readonly TimeSpan Intervalle = TimeSpan.FromSeconds(30);

    public string Name => "ProcessWatcher";
    public bool IsRunning => _isRunning;

    public event EventHandler<PatternDetectedEventArgs>? PatternDetected;

    public ProcessWatcher(IActivityContext activite, ILogger logger)
    {
        _activite = activite;
        _logger = logger;
        _checkTimer = new Timer(Verifier, null, Timeout.Infinite, Timeout.Infinite);
    }

    public void Start()
    {
        _isRunning = true;
        _checkTimer.Change(TimeSpan.FromSeconds(5), Intervalle);
        _logger.LogInformation("[ProcessWatcher] Demarre — suivi de la fenetre active toutes les {S}s",
            Intervalle.TotalSeconds);
    }

    public void Stop()
    {
        _isRunning = false;
        _checkTimer.Change(Timeout.Infinite, Timeout.Infinite);
        _logger.LogInformation("[ProcessWatcher] Arrete");
    }

    private void Verifier(object? state)
    {
        try
        {
            var nom = ApplicationAuPremierPlan();
            _activite.Signaler(nom, DateTime.UtcNow);

            if (!string.IsNullOrEmpty(nom) && !string.Equals(nom, _dernierNom, StringComparison.OrdinalIgnoreCase))
            {
                _dernierNom = nom;
                _logger.LogDebug("[ProcessWatcher] Premier plan : {App}", nom);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[ProcessWatcher] Lecture de la fenetre active impossible");
        }
    }

    /// <summary>
    /// Nom du processus propriétaire de la fenêtre active. Renvoie null si personne n'a le
    /// focus — session verrouillée, bureau vide — ce que le contexte traite comme « inconnu ».
    /// </summary>
    private static string? ApplicationAuPremierPlan()
    {
        var fenetre = GetForegroundWindow();
        if (fenetre == IntPtr.Zero) return null;

        _ = GetWindowThreadProcessId(fenetre, out var pid);
        if (pid == 0) return null;

        try
        {
            using var process = Process.GetProcessById((int)pid);
            return process.ProcessName;
        }
        catch (ArgumentException)
        {
            return null; // le processus vient de se terminer
        }
    }

    [DllImport("user32.dll")]
    private static extern IntPtr GetForegroundWindow();

    [DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint processId);
}
