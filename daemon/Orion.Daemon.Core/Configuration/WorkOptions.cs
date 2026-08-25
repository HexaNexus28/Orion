namespace Orion.Daemon.Core.Configuration;

/// <summary>
/// Ce qu'ORION surveille du TRAVAIL de l'utilisateur, par opposition à sa machine.
///
/// Les cinq watchers d'origine observent le CPU, la RAM, l'heure et l'inactivité — de l'hygiène
/// de vie. Aucun ne regarde ce qui casse réellement une journée : un service tombé, un dépôt
/// non poussé, un projet hébergé qui s'endort.
///
/// Le cas qui a motivé ce watcher : le projet Supabase d'ORION s'est mis en pause faute
/// d'activité. Résultat — plus de base, un RAG mort en silence, et des heures de diagnostic.
/// Un seul contrôle périodique aurait transformé la panne en une phrase dite trois jours avant.
/// </summary>
public class WorkOptions
{
    public bool Enabled { get; set; } = true;

    /// <summary>Intervalle entre deux rondes. Assez court pour être utile, assez long pour ne pas peser.</summary>
    public int IntervalleMinutes { get; set; } = 5;

    /// <summary>
    /// Nombre d'échecs consécutifs avant d'alerter. Un service ne « tombe » pas parce qu'une
    /// requête a expiré : sans ce seuil, chaque micro-coupure réseau devient une alerte.
    /// </summary>
    public int EchecsAvantAlerte { get; set; } = 2;

    public int TimeoutSecondes { get; set; } = 10;

    public List<ServiceSurveille> Services { get; set; } = new();

    /// <summary>Dépôts git dont on signale le travail non poussé.</summary>
    public List<string> DepotsGit { get; set; } = new();

    /// <summary>Au-delà, du travail non poussé mérite d'être signalé — au briefing, pas en urgence.</summary>
    public int JoursAvantAlerteNonPousse { get; set; } = 3;
}

public class ServiceSurveille
{
    public string Nom { get; set; } = string.Empty;
    public string Url { get; set; } = string.Empty;

    /// <summary>
    /// Un service critique émet `vps_down` (score 95) au lieu de `service_down` (90).
    /// La distinction sert la boucle de décision, pas la cosmétique.
    /// </summary>
    public bool Critique { get; set; }

    /// <summary>
    /// Codes HTTP considérés comme « vivant ». Un 401 prouve qu'un service répond —
    /// c'est exactement ce que renvoie l'API Supabase sans clé.
    /// </summary>
    public List<int> CodesVivants { get; set; } = new() { 200, 401, 403, 404 };
}
