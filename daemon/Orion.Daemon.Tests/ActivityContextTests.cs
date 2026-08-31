using Orion.Daemon.Core.Configuration;
using Orion.Daemon.Core.Interfaces;
using Orion.Daemon.Core.Proactive;

namespace Orion.Daemon.Tests;

/// <summary>
/// Le contexte d'activité est l'entrée qui manquait au score : sans lui, ORION coupait une
/// session de code pour un rappel de pause — le défaut qui fait qu'on désactive ce genre
/// d'assistant.
/// </summary>
public class ActivityContextTests
{
    private static readonly DateTime T0 = new(2026, 8, 20, 14, 0, 0, DateTimeKind.Utc);

    private static ProactiveOptions Options(int concentrationApres = 15, int fraicheur = 3) => new()
    {
        FocusAfterMinutes = concentrationApres,
        ActivityFreshnessMinutes = fraicheur,
        FocusPenalty = 25,
        InterruptionThreshold = 55,
        CriticalThreshold = 75,
        InterruptionsPerHour = 3,
        CooldownMinutes = 15
    };

    /// <summary>
    /// Rejoue ce que fait le vrai watcher : un signal toutes les 30 s. Sans cette cadence,
    /// le garde de fraicheur declare l'etat inconnu — ce qui est le comportement voulu.
    /// </summary>
    private static void Tenir(IActivityContext contexte, string app, DateTime debut, int minutes)
    {
        for (var t = 0; t <= minutes * 2; t++)
            contexte.Signaler(app, debut.AddSeconds(t * 30));
    }

    [Fact]
    public void Focus_WorkAppHeldLong_CountsAsFocus()
    {
        var contexte = new ActivityContext(Options(concentrationApres: 15));
        Tenir(contexte, "code", T0, 20);

        var etat = contexte.Etat(T0.AddMinutes(20));

        Assert.True(etat.TravailConcentre);
        Assert.Equal("code", etat.Application);
        Assert.Equal(20, (int)etat.Duree.TotalMinutes);
    }

    [Fact]
    public void Focus_FewMinutesOnWorkApp_NotEnough()
    {
        var contexte = new ActivityContext(Options(concentrationApres: 15));
        Tenir(contexte, "code", T0, 5);

        Assert.False(contexte.Etat(T0.AddMinutes(5)).TravailConcentre);
    }

    [Fact]
    public void Focus_BrowsingLong_NotFocus()
    {
        // Un navigateur ouvert depuis une heure ne dit rien : c'est l'état par défaut.
        var contexte = new ActivityContext(Options());
        Tenir(contexte, "chrome", T0, 60);

        Assert.False(contexte.Etat(T0.AddHours(1)).TravailConcentre);
    }

    [Fact]
    public void Focus_AppSwitch_ResetsCounter()
    {
        var contexte = new ActivityContext(Options(concentrationApres: 15));
        Tenir(contexte, "code", T0, 14);
        contexte.Signaler("chrome", T0.AddMinutes(14));
        Tenir(contexte, "code", T0.AddMinutes(15), 5);

        // Retour sur « code », mais le fil a été coupé : la concentration recommence.
        Assert.False(contexte.Etat(T0.AddMinutes(20)).TravailConcentre);
    }

    [Fact]
    public void Focus_StaleSignal_StateUnknown()
    {
        // Le watcher est peut-être arrêté. Suspendre les alertes sur une donnée morte serait
        // pire que d'interrompre à tort.
        var contexte = new ActivityContext(Options(concentrationApres: 1, fraicheur: 3));
        contexte.Signaler("code", T0);

        var etat = contexte.Etat(T0.AddMinutes(10));

        Assert.False(etat.TravailConcentre);
        Assert.Equal(ActivityState.Inconnu, etat);
    }

    [Fact]
    public void Focus_NoSignal_StateUnknown()
    {
        Assert.Equal(ActivityState.Inconnu, new ActivityContext(Options()).Etat(T0));
    }

    // ── Effet sur la décision ───────────────────────────────────────────────────

    private static PatternDetectedEventArgs Signal(string pattern) => new() { Pattern = pattern };

    [Fact]
    public void Threshold_DuringFocus_MediumSignalBlocked()
    {
        var options = Options();
        var contexte = new ActivityContext(options);
        var decideur = new ProactiveDecider(options, contexte);

        // `high_cpu` (60) passe le seuil normal de 55...
        Assert.Equal(ProactiveAction.Parler, decideur.Decider(Signal("high_cpu"), T0).Action);

        // ...mais plus le seuil relevé à 80 pendant une session de code.
        Tenir(contexte, "code", T0, 20);
        var pendantConcentration = decideur.Decider(Signal("high_cpu"), T0.AddMinutes(20));

        Assert.Equal(ProactiveAction.Differer, pendantConcentration.Action);
        Assert.Contains("concentre sur code", pendantConcentration.Raison);
    }

    [Fact]
    public void Threshold_DuringFocus_CriticalIncidentPasses()
    {
        // On ne laisse pas un serveur mort attendre la fin d'une session de code.
        var options = Options();
        var contexte = new ActivityContext(options);
        var decideur = new ProactiveDecider(options, contexte);

        Tenir(contexte, "code", T0, 30);
        var decision = decideur.Decider(Signal("vps_down"), T0.AddMinutes(30));

        Assert.Equal(ProactiveAction.Parler, decision.Action);
        Assert.Contains("critique", decision.Raison);
    }

    [Fact]
    public void Threshold_OutsideFocus_StaysNormal()
    {
        var options = Options();
        var contexte = new ActivityContext(options);
        var decideur = new ProactiveDecider(options, contexte);

        Tenir(contexte, "chrome", T0, 30);

        Assert.Equal(ProactiveAction.Parler, decideur.Decider(Signal("high_cpu"), T0.AddMinutes(30)).Action);
    }

    [Fact]
    public void Decide_NoContextSupplied_DeciderStillWorks()
    {
        // Aveugle à l'activité, mais correct : l'absence de contexte ne doit rien casser.
        var decideur = new ProactiveDecider(Options());

        Assert.Equal(ProactiveAction.Parler, decideur.Decider(Signal("high_cpu"), T0).Action);
    }
}
