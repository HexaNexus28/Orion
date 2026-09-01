using Orion.Daemon.Core.Configuration;
using Orion.Daemon.Core.Proactive;

namespace Orion.Daemon.Tests;

/// <summary>
/// Le surmenage se mesure sur du travail CONTINU. ActivityWatcher le mesurait sur du temps
/// d'INACTIVITE : il annoncait « temps de pause » a quelqu'un qui n'avait pas touche la machine
/// depuis six heures. Son propre message le disait — « Inactif depuis 6,0h - temps de pause » —
/// et personne ne l'avait relu.
///
/// Ces tests verrouillent les deux moities du contrat : signaler un vrai surmenage, et ne PAS
/// signaler une absence.
/// </summary>
public class WorkSessionTrackerTests
{
    private static readonly DateTime T0 = new(2026, 9, 1, 9, 0, 0, DateTimeKind.Utc);

    private static WorkSessionTracker Build(int hours = 3, int breakMinutes = 10)
        => new(new ProactiveOptions { OverworkAfterHours = hours, BreakMinutes = breakMinutes });

    /// <summary>
    /// Fait battre le suivi a sa cadence REELLE — 30 s — de <paramref name="depuis"/> a
    /// <paramref name="jusqua"/>. Sauter d'une heure entre deux appels ne testerait pas le
    /// systeme reel : le suivi interprete un trou comme une absence de mesure, pas comme du
    /// travail. Rend la premiere severite signalee, ou null.
    /// </summary>
    private static int? Battre(WorkSessionTracker tracker, string? app, DateTime depuis, DateTime jusqua)
    {
        int? premiere = null;
        for (var t = depuis; t <= jusqua; t = t.AddSeconds(30))
        {
            var s = tracker.Tick(app, t);
            premiere ??= s;
        }
        return premiere;
    }

    /// <summary>Toutes les severites signalees sur l'intervalle, dans l'ordre.</summary>
    private static List<int> Signaux(WorkSessionTracker tracker, string? app, DateTime depuis, DateTime jusqua)
    {
        var tous = new List<int>();
        for (var t = depuis; t <= jusqua; t = t.AddSeconds(30))
        {
            if (tracker.Tick(app, t) is { } s) tous.Add(s);
        }
        return tous;
    }

    [Fact]
    public void Tick_ShortSession_StaysSilent()
    {
        var tracker = Build(hours: 3);

        Assert.Null(Battre(tracker, "code", T0, T0.AddHours(2)));
    }

    [Fact]
    public void Tick_ContinuousWorkPastThreshold_Signals()
    {
        var tracker = Build(hours: 3);

        Assert.NotNull(Battre(tracker, "code", T0, T0.AddHours(3).AddMinutes(1)));
    }

    [Fact]
    public void Tick_AppSwitchWithinWork_DoesNotResetSession()
    {
        // ActivityContext repart a zero a chaque changement d'application : c'est ce qu'il faut
        // pour juger la concentration, jamais pour juger le surmenage. Passer de l'editeur au
        // terminal, c'est toujours travailler.
        var tracker = Build(hours: 3);

        Battre(tracker, "code", T0, T0.AddHours(1));
        Battre(tracker, "windowsterminal", T0.AddHours(1), T0.AddHours(2));

        Assert.NotNull(Battre(tracker, "code", T0.AddHours(2), T0.AddHours(3).AddMinutes(1)));
    }

    [Fact]
    public void Tick_ShortNonWorkDetour_DoesNotResetSession()
    {
        // Aller lire une page web cinq minutes n'est pas une pause.
        var tracker = Build(hours: 3, breakMinutes: 10);

        Battre(tracker, "code", T0, T0.AddHours(1));
        Battre(tracker, "chrome", T0.AddHours(1), T0.AddHours(1).AddMinutes(5));

        Assert.NotNull(Battre(tracker, "code", T0.AddHours(1).AddMinutes(5), T0.AddHours(3).AddMinutes(1)));
    }

    [Fact]
    public void Tick_RealBreak_ResetsSession()
    {
        var tracker = Build(hours: 3, breakMinutes: 10);

        Battre(tracker, "code", T0, T0.AddHours(1));
        Battre(tracker, "chrome", T0.AddHours(1), T0.AddHours(1).AddMinutes(30));   // vraie pause

        // La session repart de zero : trois heures apres T0, on est loin du seuil.
        Assert.Null(Battre(tracker, "code", T0.AddHours(1).AddMinutes(30), T0.AddHours(3).AddMinutes(1)));
    }

    [Fact]
    public void Tick_AbsentUser_NeverSignals()
    {
        // LE test qui verrouille l'inversion corrigee : personne au clavier ne doit RIEN
        // declencher, meme au bout de huit heures.
        var tracker = Build(hours: 3);

        Assert.Null(Battre(tracker, null, T0, T0.AddHours(8)));
    }

    [Fact]
    public void Tick_PastThreshold_AtMostOneReminderPerHour()
    {
        // Un rappel par heure au maximum : le seuil ne se refranchit pas a chaque battement.
        // Le cooldown du decideur est une seconde barriere, pas la premiere.
        var tracker = Build(hours: 3);

        var signaux = Signaux(tracker, "code", T0, T0.AddHours(3).AddMinutes(30));

        Assert.Single(signaux);
    }

    [Fact]
    public void Tick_LongerSession_HigherSeverity()
    {
        // Le signal se REPETE en s'aggravant : sans rappel, la severite serait mesuree au seul
        // franchissement du seuil et vaudrait toujours zero — elle ne dirait jamais que trois
        // heures sont devenues six. C'est ce que ce test a fait apparaitre.
        var tracker = Build(hours: 3);

        var signaux = Signaux(tracker, "code", T0, T0.AddHours(6));

        Assert.True(signaux.Count >= 3, $"attendu au moins 3 rappels, obtenu {signaux.Count}");
        Assert.Equal(0, signaux[0]);
        Assert.True(signaux[^1] > signaux[0], $"attendu {signaux[^1]} > {signaux[0]}");
        Assert.Equal(100, signaux[^1]);
    }

    [Fact]
    public void Duration_AfterBreak_BackToZero()
    {
        var tracker = Build(hours: 3, breakMinutes: 10);

        Battre(tracker, "code", T0, T0.AddHours(1));
        Assert.Equal(TimeSpan.FromHours(1), tracker.Duration(T0.AddHours(1)));

        Battre(tracker, "chrome", T0.AddHours(1), T0.AddHours(2));

        Assert.Equal(TimeSpan.Zero, tracker.Duration(T0.AddHours(2)));
    }
}
