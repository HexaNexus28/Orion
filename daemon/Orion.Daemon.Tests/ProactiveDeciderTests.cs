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
        InterruptionsPerHour = parHeure,
        CooldownMinutes = cooldown,
        InterruptionThreshold = 55,
        CriticalThreshold = 75
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
    public void Decide_HighCpuBarelyOverThreshold_GoesToBriefing()
    {
        // LE bug du 2026-09-01 : SystemWatcher ecrivait `cpu_percent`, le decideur lit
        // `severity`. Le contrat n'etait relie nulle part, le score restait fige a 60 — au-dessus
        // du seuil de 55 — et un CPU a 90,1 % interrompait aussi fort qu'un CPU sature.
        var decideur = new ProactiveDecider(Options());

        var decision = decideur.Decider(Signal("high_cpu", severite: 1), T0);

        Assert.Equal(ProactiveAction.Differer, decision.Action);
        Assert.True(decision.Score < 55, $"score attendu sous le seuil, obtenu {decision.Score}");
    }

    [Fact]
    public void Decide_HighCpuSaturated_Interrupts()
    {
        // La contrepartie, sans laquelle le test precedent serait satisfait par un watcher muet.
        var decideur = new ProactiveDecider(Options());

        var decision = decideur.Decider(Signal("high_cpu", severite: 100), T0);

        Assert.Equal(ProactiveAction.Parler, decision.Action);
        Assert.True(decision.Score > 55, $"score attendu au-dessus du seuil, obtenu {decision.Score}");
    }

    [Fact]
    public void Decide_Incident_Interrupts()
    {
        var decideur = new ProactiveDecider(Options());

        var decision = decideur.Decider(Signal("vps_down"), T0);

        Assert.Equal(ProactiveAction.Parler, decision.Action);
        Assert.True(decision.Score >= 75);
    }

    [Fact]
    public void Decide_WellbeingReminder_DoesNotInterrupt()
    {
        // « C'est l'heure de manger » est vrai, mais ne vaut pas qu'on coupe une session de
        // travail. Ça attend le briefing.
        var decideur = new ProactiveDecider(Options());

        var decision = decideur.Decider(Signal("meal_time"), T0);

        Assert.Equal(ProactiveAction.Differer, decision.Action);
    }

    [Fact]
    public void Decide_SameSignalWithinCooldown_Suppressed()
    {
        var decideur = new ProactiveDecider(Options(cooldown: 15));

        Assert.Equal(ProactiveAction.Parler, Appliquer(decideur, Signal("vps_down"), T0).Action);

        var suivant = decideur.Decider(Signal("vps_down"), T0.AddMinutes(5));

        Assert.Equal(ProactiveAction.Taire, suivant.Action);
        Assert.Contains("deja signale", suivant.Raison);
    }

    [Fact]
    public void Decide_AfterCooldown_SignalPassesAgain()
    {
        var decideur = new ProactiveDecider(Options(cooldown: 15));
        Appliquer(decideur, Signal("vps_down"), T0);

        var suivant = decideur.Decider(Signal("vps_down"), T0.AddMinutes(16));

        Assert.Equal(ProactiveAction.Parler, suivant.Action);
    }

    [Fact]
    public void Decide_AttentionBudget_CapsInterruptions()
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
    public void Decide_CriticalIncident_PassesEvenWhenBudgetSpent()
    {
        // Perdre une alerte de service mort pour cause de quota serait absurde.
        var decideur = new ProactiveDecider(Options(parHeure: 1));
        Appliquer(decideur, Signal("high_cpu"), T0);

        var critique = decideur.Decider(Signal("vps_down"), T0.AddMinutes(1));

        Assert.Equal(ProactiveAction.Parler, critique.Action);
        Assert.Contains("critique", critique.Raison);
    }

    [Fact]
    public void Decide_OverTime_BudgetFreesUp()
    {
        var decideur = new ProactiveDecider(Options(parHeure: 1));
        Appliquer(decideur, Signal("high_ram"), T0);

        // Plus d'une heure après : la fenêtre glissante a laissé passer la première.
        var suivant = decideur.Decider(Signal("high_cpu"), T0.AddHours(1).AddMinutes(1));

        Assert.Equal(ProactiveAction.Parler, suivant.Action);
    }

    [Fact]
    public void Decide_DeferredSignal_AppearsInBriefing()
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
    public void Drain_Called_EmptiesQueue()
    {
        var decideur = new ProactiveDecider(Options());
        Appliquer(decideur, Signal("meal_time"), T0);

        Assert.Single(decideur.DrainerDifferes());
        Assert.Empty(decideur.DrainerDifferes());
    }

    [Fact]
    public void Decide_SignalDeferredRepeatedly_DoesNotPileUp()
    {
        // Sinon le briefing répète dix fois « c'est l'heure de manger ».
        var decideur = new ProactiveDecider(Options());
        Appliquer(decideur, Signal("meal_time"), T0);
        Appliquer(decideur, Signal("meal_time"), T0.AddMinutes(30));
        Appliquer(decideur, Signal("meal_time"), T0.AddHours(2));

        Assert.Single(decideur.DrainerDifferes());
    }

    [Fact]
    public void Decide_MeasuredSeverity_ShiftsScoreKeepsRanking()
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
    public void Decide_LearnedPenalty_SilencesPassingSignal()
    {
        var decideur = new ProactiveDecider(Options());
        Assert.Equal(ProactiveAction.Parler, decideur.Decider(Signal("high_cpu"), T0).Action);

        // L'utilisateur a refuse ce signal : sa penalite le fait passer sous le seuil.
        decideur.AppliquerPenalites(new Dictionary<string, int> { ["high_cpu"] = 20 });

        Assert.Equal(ProactiveAction.Differer, decideur.Decider(Signal("high_cpu"), T0).Action);
    }

    [Fact]
    public void Decide_StrongPenalty_DropsCriticalBelowThreshold()
    {
        // Un utilisateur qui refuse obstinement doit pouvoir faire taire meme une alerte forte.
        var decideur = new ProactiveDecider(Options());
        Assert.Equal(ProactiveAction.Parler, decideur.Decider(Signal("vps_down"), T0).Action);

        decideur.AppliquerPenalites(new Dictionary<string, int> { ["vps_down"] = 60 });

        Assert.Equal(ProactiveAction.Differer, decideur.Decider(Signal("vps_down"), T0).Action);
    }

    [Fact]
    public void Decide_Penalty_AffectsOnlyItsPattern()
    {
        var decideur = new ProactiveDecider(Options());
        decideur.AppliquerPenalites(new Dictionary<string, int> { ["high_cpu"] = 50 });

        Assert.Equal(ProactiveAction.Parler, decideur.Decider(Signal("high_ram"), T0).Action);
    }

    [Fact]
    public void Decide_NoLearnedPenalty_BehaviourUnchanged()
    {
        var decideur = new ProactiveDecider(Options());
        decideur.AppliquerPenalites(new Dictionary<string, int>());

        Assert.Equal(ProactiveAction.Parler, decideur.Decider(Signal("vps_down"), T0).Action);
    }

    [Fact]
    public void Decide_UnknownPattern_GetsMediumUrgency()
    {
        var decideur = new ProactiveDecider(Options());

        var decision = decideur.Decider(Signal("pattern_jamais_vu"), T0);

        Assert.Equal(ProactiveAction.Differer, decision.Action);
        Assert.InRange(decision.Score, 1, 74);
    }
}
