using System.Diagnostics;
using Microsoft.Extensions.Logging;
using Orion.Daemon.Core.Configuration;
using Orion.Daemon.Core.Interfaces;

namespace Orion.Daemon.Watchers;

/// <summary>
/// Surveille le TRAVAIL de l'utilisateur, pas sa machine : services joignables, dépôts non
/// poussés. C'est le seul watcher dont une alerte évite une panne au lieu de rappeler de manger.
///
/// Fonctionne en continu, par rondes. Il n'alerte qu'après plusieurs échecs consécutifs :
/// un service ne « tombe » pas parce qu'une requête a expiré.
/// </summary>
public class WorkWatcher : IWatcher
{
    private readonly WorkOptions _options;
    private readonly ILogger _logger;
    private readonly HttpClient _http;
    private readonly Timer _timer;

    /// <summary>Échecs consécutifs par service — un incident se confirme, il ne se devine pas.</summary>
    private readonly Dictionary<string, int> _echecs = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Services actuellement signalés en panne : sert à annoncer le RÉTABLISSEMENT.</summary>
    private readonly HashSet<string> _enPanne = new(StringComparer.OrdinalIgnoreCase);

    private bool _isRunning;

    public string Name => "WorkWatcher";
    public bool IsRunning => _isRunning;

    public event EventHandler<PatternDetectedEventArgs>? PatternDetected;

    public WorkWatcher(WorkOptions options, ILogger logger)
    {
        _options = options;
        _logger = logger;
        _http = new HttpClient { Timeout = TimeSpan.FromSeconds(options.TimeoutSecondes) };
        _timer = new Timer(async _ => await RondeAsync(), null, Timeout.Infinite, Timeout.Infinite);
    }

    public void Start()
    {
        if (!_options.Enabled)
        {
            _logger.LogInformation("[WorkWatcher] Desactive par configuration");
            return;
        }

        _isRunning = true;
        var intervalle = TimeSpan.FromMinutes(Math.Max(1, _options.IntervalleMinutes));

        // Première ronde après 30 s : on laisse le système finir de démarrer avant de crier
        // que tout est cassé.
        _timer.Change(TimeSpan.FromSeconds(30), intervalle);

        _logger.LogInformation("[WorkWatcher] Demarre — {Services} service(s), {Depots} depot(s), ronde toutes les {Min} min",
            _options.Services.Count, _options.DepotsGit.Count, intervalle.TotalMinutes);
    }

    public void Stop()
    {
        _isRunning = false;
        _timer.Change(Timeout.Infinite, Timeout.Infinite);
        _logger.LogInformation("[WorkWatcher] Arrete");
    }

    private async Task RondeAsync()
    {
        try
        {
            foreach (var service in _options.Services)
                await VerifierServiceAsync(service);

            foreach (var depot in _options.DepotsGit)
                VerifierDepot(depot);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[WorkWatcher] Ronde en echec");
        }
    }

    private async Task VerifierServiceAsync(ServiceSurveille service)
    {
        if (string.IsNullOrWhiteSpace(service.Url)) return;

        var vivant = await EstVivantAsync(service);

        if (vivant)
        {
            // Rétablissement : on ne se contente pas d'arrêter d'alerter, on le DIT.
            // Un incident qui disparaît sans un mot laisse l'utilisateur dans le doute.
            if (_enPanne.Remove(service.Nom))
            {
                _logger.LogInformation("[WorkWatcher] {Service} est de nouveau joignable", service.Nom);
                Emettre("service_restored",
                    $"{service.Nom} est de nouveau joignable.",
                    new() { ["service"] = service.Nom, ["severity"] = 20 });
            }

            _echecs[service.Nom] = 0;
            return;
        }

        var consecutifs = _echecs.TryGetValue(service.Nom, out var n) ? n + 1 : 1;
        _echecs[service.Nom] = consecutifs;

        _logger.LogWarning("[WorkWatcher] {Service} injoignable ({N} echec(s) consecutif(s))",
            service.Nom, consecutifs);

        if (consecutifs < _options.EchecsAvantAlerte) return;
        if (!_enPanne.Add(service.Nom)) return; // déjà signalé, le cooldown central fera le reste

        Emettre(
            service.Critique ? "vps_down" : "service_down",
            $"{service.Nom} ne repond plus depuis {consecutifs} verification(s).",
            new() { ["service"] = service.Nom, ["severity"] = service.Critique ? 100 : 80 });
    }

    private async Task<bool> EstVivantAsync(ServiceSurveille service)
    {
        try
        {
            using var requete = new HttpRequestMessage(HttpMethod.Get, service.Url);
            using var reponse = await _http.SendAsync(requete, HttpCompletionOption.ResponseHeadersRead);

            // Un 401 prouve qu'un service RÉPOND — c'est le cas de l'API Supabase sans clé.
            // Confondre « refuse » et « mort » produirait une fausse alerte permanente.
            return service.CodesVivants.Contains((int)reponse.StatusCode);
        }
        catch
        {
            return false;
        }
    }

    private void VerifierDepot(string chemin)
    {
        if (!Directory.Exists(chemin)) return;

        var branche = Git(chemin, "rev-parse --abbrev-ref HEAD");
        if (string.IsNullOrWhiteSpace(branche)) return;

        // Aucun amont : rien n'est « non poussé », il n'y a nulle part où pousser.
        var amont = Git(chemin, $"rev-parse --abbrev-ref {branche}@{{upstream}}");
        if (string.IsNullOrWhiteSpace(amont)) return;

        var commits = Git(chemin, $"rev-list --count {amont}..{branche}");
        if (!int.TryParse(commits, out var enAttente) || enAttente == 0) return;

        // L'âge du plus ancien commit non poussé : c'est lui qui mesure le risque réel.
        var dateBrute = Git(chemin, $"log -1 --format=%cI {amont}..{branche} --reverse");
        if (!DateTimeOffset.TryParse(dateBrute, out var plusAncien)) return;

        var jours = (int)(DateTimeOffset.UtcNow - plusAncien).TotalDays;
        if (jours < _options.JoursAvantAlerteNonPousse) return;

        var nom = new DirectoryInfo(chemin).Name;
        Emettre("unpushed_work",
            $"{nom} : {enAttente} commit(s) non pousse(s) sur {branche}, le plus ancien date de {jours} jour(s).",
            new() { ["depot"] = nom, ["commits"] = enAttente, ["severity"] = Math.Clamp(jours * 8, 0, 100) });
    }

    private string Git(string chemin, string arguments)
    {
        try
        {
            using var process = Process.Start(new ProcessStartInfo("git", arguments)
            {
                WorkingDirectory = chemin,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            });

            if (process is null) return string.Empty;

            var sortie = process.StandardOutput.ReadToEnd().Trim();
            process.WaitForExit(5000);

            return process.ExitCode == 0 ? sortie : string.Empty;
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "[WorkWatcher] git {Args} a echoue dans {Chemin}", arguments, chemin);
            return string.Empty;
        }
    }

    private void Emettre(string pattern, string contexte, Dictionary<string, object> metadata)
    {
        PatternDetected?.Invoke(this, new PatternDetectedEventArgs
        {
            Pattern = pattern,
            Context = contexte,
            Metadata = metadata
        });
    }
}
