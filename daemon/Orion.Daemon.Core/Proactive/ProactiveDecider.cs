using Orion.Daemon.Core.Configuration;
using Orion.Daemon.Core.Interfaces;

namespace Orion.Daemon.Core.Proactive;

/// <summary>
/// Décide si un signal mérite d'interrompre, d'attendre le briefing, ou de se taire.
///
/// Logique PURE : aucune I/O, l'horloge est injectée. C'est le cerveau de la proactivité,
/// il doit être testable sans démarrer un daemon.
///
/// Trois filtres, dans cet ordre — le moins cher d'abord :
///   1. cooldown  : on vient déjà de le dire ;
///   2. seuil     : c'est vrai, mais est-ce que ça vaut ton attention MAINTENANT ;
///   3. budget    : combien d'interruptions ORION s'est-il déjà autorisées cette heure.
/// </summary>
public class ProactiveDecider : IProactiveDecider
{
    private readonly ProactiveOptions _options;
    private readonly IActivityContext _activite;

    private readonly Dictionary<string, DateTime> _derniereParole = new(StringComparer.OrdinalIgnoreCase);
    private readonly List<DateTime> _interruptions = new();
    private readonly List<SignalDiffere> _differes = new();

    /// <summary>Pénalités apprises, rafraîchies périodiquement depuis le backend.</summary>
    private Dictionary<string, int> _penalites = new(StringComparer.OrdinalIgnoreCase);
    private readonly object _verrou = new();

    public ProactiveDecider(ProactiveOptions options, IActivityContext? activite = null)
    {
        _options = options;
        // Sans contexte fourni, un contexte neutre : la décision reste correcte, simplement
        // aveugle à ce que fait l'utilisateur.
        _activite = activite ?? new ActivityContext(options);
    }

    /// <summary>
    /// Urgence de base par pattern, de 0 à 100.
    ///
    /// Le classement dit une chose simple : ce qui MENACE le travail en cours passe devant ce
    /// qui relève de l'hygiène de vie. Une RAM saturée fait perdre le travail ; un rappel de
    /// repas peut attendre le prochain café.
    /// </summary>
    private static readonly Dictionary<string, int> Urgences = new(StringComparer.OrdinalIgnoreCase)
    {
        // Incidents — ils menacent le travail ou les données
        ["vps_down"] = 95,
        ["service_down"] = 90,
        ["build_failed"] = 80,
        ["supabase_idle"] = 75,   // le projet va se mettre en pause : panne évitable
        ["high_ram"] = 70,
        ["high_cpu"] = 60,
        ["unpushed_work"] = 45,
        ["service_restored"] = 20,   // bonne nouvelle : elle attend le briefing

        // Hygiène de vie — vrai, mais jamais urgent
        ["overwork"] = 40,
        ["skip_meal"] = 30,
        ["night_time"] = 25,
        ["meal_time"] = 20,
        ["break_time"] = 15,
        ["adaptive_morning_routine"] = 10,
    };

    private const int UrgenceInconnue = 35;

    public void AppliquerPenalites(IReadOnlyDictionary<string, int> penalites)
    {
        lock (_verrou)
        {
            _penalites = new Dictionary<string, int>(penalites, StringComparer.OrdinalIgnoreCase);
        }
    }

    public ProactiveDecision Decider(PatternDetectedEventArgs signal, DateTime maintenant)
    {
        int penalite;
        lock (_verrou)
        {
            _penalites.TryGetValue(signal.Pattern, out penalite);
        }

        var score = Math.Clamp(Scorer(signal) - penalite, 0, 100);

        lock (_verrou)
        {
            // 1. Vient-on déjà de le dire ?
            if (_derniereParole.TryGetValue(signal.Pattern, out var derniere))
            {
                var repos = TimeSpan.FromMinutes(_options.CooldownMinutes);
                if (maintenant - derniere < repos)
                {
                    return ProactiveDecision.Taire(score,
                        $"deja signale il y a {(int)(maintenant - derniere).TotalMinutes} min");
                }
            }

            // 2. Un incident critique passe TOUJOURS, budget ou non.
            //    Perdre une alerte de service mort pour cause de quota serait absurde.
            //    Le score a DEJA subi la penalite apprise : un utilisateur qui refuse
            //    obstinement un signal peut donc le faire descendre sous ce seuil.
            if (score >= _options.CriticalThreshold)
                return ProactiveDecision.Parler(score, "incident critique");

            // 3. Sous le seuil d'interruption : vrai, mais ça attendra le briefing.
            //    Le seuil MONTE pendant une session concentrée : reprendre le fil d'un
            //    raisonnement de code coûte bien plus qu'une lecture interrompue. Les incidents
            //    critiques sont déjà passés à l'étape 2, ils ne sont donc jamais bloqués ici.
            var etat = _activite.Etat(maintenant);
            var seuil = etat.TravailConcentre
                ? _options.InterruptionThreshold + _options.FocusPenalty
                : _options.InterruptionThreshold;

            if (score < seuil)
            {
                return ProactiveDecision.Differer(score, etat.TravailConcentre
                    ? $"concentre sur {etat.Application} depuis {(int)etat.Duree.TotalMinutes} min — seuil releve a {seuil}"
                    : "pas assez urgent pour interrompre");
            }

            // 4. Budget d'attention — ce qui sépare un collègue d'un spammeur de notifications.
            var depuisUneHeure = _interruptions.Count(i => maintenant - i < TimeSpan.FromHours(1));
            if (depuisUneHeure >= _options.InterruptionsPerHour)
            {
                return ProactiveDecision.Differer(score,
                    $"budget d'attention epuise ({depuisUneHeure}/{_options.InterruptionsPerHour} cette heure)");
            }

            return ProactiveDecision.Parler(score, "au-dessus du seuil, budget disponible");
        }
    }

    public void Enregistrer(PatternDetectedEventArgs signal, ProactiveDecision decision, DateTime maintenant)
    {
        lock (_verrou)
        {
            switch (decision.Action)
            {
                case ProactiveAction.Parler:
                    _derniereParole[signal.Pattern] = maintenant;
                    _interruptions.Add(maintenant);
                    // On ne garde qu'une fenêtre glissante : la liste ne doit pas croître sans fin.
                    _interruptions.RemoveAll(i => maintenant - i > TimeSpan.FromHours(2));
                    break;

                case ProactiveAction.Differer:
                    // Un même pattern différé plusieurs fois ne s'accumule pas : on garde le
                    // plus récent, sinon le briefing répète dix fois la même chose.
                    _differes.RemoveAll(d => string.Equals(d.Pattern, signal.Pattern, StringComparison.OrdinalIgnoreCase));
                    _differes.Add(new SignalDiffere(signal.Pattern, signal.Context, decision.Score, maintenant));
                    break;
            }
        }
    }

    public IReadOnlyList<SignalDiffere> DrainerDifferes()
    {
        lock (_verrou)
        {
            var copie = _differes.OrderByDescending(d => d.Score).ToList();
            _differes.Clear();
            return copie;
        }
    }

    /// <summary>
    /// Score = urgence de base, ajustée par ce que le watcher a mesuré.
    /// Un watcher peut fournir `severity` (0-100) pour affiner : une RAM à 99 % n'est pas
    /// une RAM à 86 %.
    /// </summary>
    private static int Scorer(PatternDetectedEventArgs signal)
    {
        var baseScore = Urgences.TryGetValue(signal.Pattern, out var u) ? u : UrgenceInconnue;

        if (signal.Metadata.TryGetValue("severity", out var brut)
            && int.TryParse(brut?.ToString(), out var severite))
        {
            // La sévérité mesurée pèse un tiers : elle module, elle ne renverse pas le classement.
            baseScore = (int)Math.Round(baseScore * 0.67 + Math.Clamp(severite, 0, 100) * 0.33);
        }

        return Math.Clamp(baseScore, 0, 100);
    }
}
