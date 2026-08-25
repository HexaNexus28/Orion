using Orion.Daemon.Core.Configuration;
using Orion.Daemon.Core.Interfaces;
using Orion.Daemon.Core.Proactive;

namespace Orion.Daemon.Tests;

/// <summary>
/// La boucle de décision est le cerveau de la proactivité. Avant elle, TOUT pattern détecté
/// devenait une parole, immédiatement et sans condition — ORION n'avait aucun moyen de se taire.
///
/// Ce qui rend une proactivité supportable dans la durée n'est pas sa capacité à parler,
/// c'est sa capacité à s'abstenir. Ces tests verrouillent l'abstention.
/// </summary>
public class ProactiveDeciderTests
{
    private static readonly DateTime T0 = new(2026, 8, 20, 14, 0, 0, DateTimeKind.Utc);

    private static ProactiveOptions Options(int parHeure = 3, int cooldown = 15) => new()
    {
        InterruptionsParHeure = parHeure,
        CooldownMinutes = cooldown,
        SeuilInterruption = 55,
        SeuilCritique = 75
    };

    private static PatternDetectedEventArgs Signal(string pattern, int? severite = null)
    {
        var e = new PatternDetectedEventArgs { Pattern = pattern, Context = $"contexte {pattern}" };
        if (severite.HasValue) e.Metadata["severity"] = severite.Value;
        return e;
    }

    private static ProactiveDecision Appliquer(IProactiveDecider d, PatternDetectedEventArgs s, DateTime t)
    {
        var decision = d.Decider(s, t);
        d.Enregistrer(s, decision, t);
        return decision;
    }

    [Fact]
    public void Un_incident_interrompt()
    {
        var decideur = new ProactiveDecider(Options());

        var decision = decideur.Decider(Signal("vps_down"), T0);

        Assert.Equal(ProactiveAction.Parler, decision.Action);
        Assert.True(decision.Score >= 75);
    }

    [Fact]
    public void Un_rappel_d_hygiene_de_vie_n_interrompt_PAS()
    {
        // « C'est l'heure de manger » est vrai, mais ne vaut pas qu'on coupe une session de
        // travail. Ça attend le briefing.
        var decideur = new ProactiveDecider(Options());

        var decision = decideur.Decider(Signal("meal_time"), T0);

        Assert.Equal(ProactiveAction.Differer, decision.Action);
    }

    [Fact]
    public void Le_meme_signal_ne_se_repete_pas_dans_le_cooldown()
    {
        var decideur = new ProactiveDecider(Options(cooldown: 15));

        Assert.Equal(ProactiveAction.Parler, Appliquer(decideur, Signal("vps_down"), T0).Action);

        var suivant = decideur.Decider(Signal("vps_down"), T0.AddMinutes(5));

        Assert.Equal(ProactiveAction.Taire, suivant.Action);
        Assert.Contains("deja signale", suivant.Raison);
    }

    [Fact]
    public void Passe_le_cooldown_le_signal_repasse()
    {
        var decideur = new ProactiveDecider(Options(cooldown: 15));
        Appliquer(decideur, Signal("vps_down"), T0);

        var suivant = decideur.Decider(Signal("vps_down"), T0.AddMinutes(16));

        Assert.Equal(ProactiveAction.Parler, suivant.Action);
    }

    [Fact]
    public void Le_budget_d_attention_borne_les_interruptions()
    {
        // Ce qui sépare un collègue d'un spammeur de notifications.
        var decideur = new ProactiveDecider(Options(parHeure: 2));

        // Trois patterns DIFFERENTS, donc le cooldown ne joue pas — seul le budget compte.
        Assert.Equal(ProactiveAction.Parler, Appliquer(decideur, Signal("high_ram"), T0).Action);
        Assert.Equal(ProactiveAction.Parler, Appliquer(decideur, Signal("high_cpu"), T0.AddMinutes(1)).Action);

        var troisieme = Appliquer(decideur, Signal("unpushed_work", severite: 90), T0.AddMinutes(2));

        Assert.Equal(ProactiveAction.Differer, troisieme.Action);
        Assert.Contains("budget", troisieme.Raison);
    }

    [Fact]
    public void Un_incident_CRITIQUE_passe_meme_budget_epuise()
    {
        // Perdre une alerte de service mort pour cause de quota serait absurde.
        var decideur = new ProactiveDecider(Options(parHeure: 1));
        Appliquer(decideur, Signal("high_cpu"), T0);

        var critique = decideur.Decider(Signal("vps_down"), T0.AddMinutes(1));

        Assert.Equal(ProactiveAction.Parler, critique.Action);
        Assert.Contains("critique", critique.Raison);
    }

    [Fact]
    public void Le_budget_se_libere_avec_le_temps()
    {
        var decideur = new ProactiveDecider(Options(parHeure: 1));
        Appliquer(decideur, Signal("high_ram"), T0);

        // Plus d'une heure après : la fenêtre glissante a laissé passer la première.
        var suivant = decideur.Decider(Signal("high_cpu"), T0.AddHours(1).AddMinutes(1));

        Assert.Equal(ProactiveAction.Parler, suivant.Action);
    }

    [Fact]
    public void Ce_qui_est_differe_se_retrouve_dans_le_briefing()
    {
        var decideur = new ProactiveDecider(Options());
        Appliquer(decideur, Signal("meal_time"), T0);
        Appliquer(decideur, Signal("break_time"), T0.AddMinutes(1));

        var differes = decideur.DrainerDifferes();

        Assert.Equal(2, differes.Count);
        // Trié par score : le plus important en tête du briefing.
        Assert.True(differes[0].Score >= differes[1].Score);
    }

    [Fact]
    public void Drainer_vide_la_file()
    {
        var decideur = new ProactiveDecider(Options());
        Appliquer(decideur, Signal("meal_time"), T0);

        Assert.Single(decideur.DrainerDifferes());
        Assert.Empty(decideur.DrainerDifferes());
    }

    [Fact]
    public void Un_signal_differe_plusieurs_fois_ne_s_accumule_pas()
    {
        // Sinon le briefing répète dix fois « c'est l'heure de manger ».
        var decideur = new ProactiveDecider(Options());
        Appliquer(decideur, Signal("meal_time"), T0);
        Appliquer(decideur, Signal("meal_time"), T0.AddMinutes(30));
        Appliquer(decideur, Signal("meal_time"), T0.AddHours(2));

        Assert.Single(decideur.DrainerDifferes());
    }

    [Fact]
    public void La_severite_mesuree_module_le_score_sans_renverser_le_classement()
    {
        var decideur = new ProactiveDecider(Options());

        var ramCritique = decideur.Decider(Signal("high_ram", severite: 100), T0);
        var ramModeree = decideur.Decider(Signal("high_ram", severite: 0), T0);

        Assert.True(ramCritique.Score > ramModeree.Score);
        // Même à sévérité nulle, une RAM saturée reste plus urgente qu'un rappel de pause.
        Assert.True(ramModeree.Score > decideur.Decider(Signal("break_time"), T0).Score);
    }

    // ── Apprentissage : ORION cesse de dire ce qu'on ignore ─────────────────────

    [Fact]
    public void Une_penalite_apprise_fait_taire_un_signal_qui_passait()
    {
        var decideur = new ProactiveDecider(Options());
        Assert.Equal(ProactiveAction.Parler, decideur.Decider(Signal("high_cpu"), T0).Action);

        // L'utilisateur a refuse ce signal : sa penalite le fait passer sous le seuil.
        decideur.AppliquerPenalites(new Dictionary<string, int> { ["high_cpu"] = 20 });

        Assert.Equal(ProactiveAction.Differer, decideur.Decider(Signal("high_cpu"), T0).Action);
    }

    [Fact]
    public void Une_penalite_forte_fait_descendre_un_signal_CRITIQUE_sous_le_seuil()
    {
        // Un utilisateur qui refuse obstinement doit pouvoir faire taire meme une alerte forte.
        var decideur = new ProactiveDecider(Options());
        Assert.Equal(ProactiveAction.Parler, decideur.Decider(Signal("vps_down"), T0).Action);

        decideur.AppliquerPenalites(new Dictionary<string, int> { ["vps_down"] = 60 });

        Assert.Equal(ProactiveAction.Differer, decideur.Decider(Signal("vps_down"), T0).Action);
    }

    [Fact]
    public void Une_penalite_ne_touche_QUE_le_pattern_concerne()
    {
        var decideur = new ProactiveDecider(Options());
        decideur.AppliquerPenalites(new Dictionary<string, int> { ["high_cpu"] = 50 });

        Assert.Equal(ProactiveAction.Parler, decideur.Decider(Signal("high_ram"), T0).Action);
    }

    [Fact]
    public void Sans_penalite_apprise_le_comportement_est_inchange()
    {
        var decideur = new ProactiveDecider(Options());
        decideur.AppliquerPenalites(new Dictionary<string, int>());

        Assert.Equal(ProactiveAction.Parler, decideur.Decider(Signal("vps_down"), T0).Action);
    }

    [Fact]
    public void Un_pattern_inconnu_recoit_une_urgence_moyenne_et_ne_casse_rien()
    {
        var decideur = new ProactiveDecider(Options());

        var decision = decideur.Decider(Signal("pattern_jamais_vu"), T0);

        Assert.Equal(ProactiveAction.Differer, decision.Action);
        Assert.InRange(decision.Score, 1, 74);
    }
}
