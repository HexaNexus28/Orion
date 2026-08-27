using Moq;
using Orion.Business.Tools.System;
using Orion.Core.DTOs.Responses;
using Orion.Core.Interfaces.Daemon;

namespace Orion.Tests.Tools;

/// <summary>
/// Le widget « Contexte ». Ces cas viennent de titres REELS releves le 2026-08-27 : chaque
/// application ordonne ses segments differemment, et une premiere version basee sur la position
/// annoncait « fichier : tiktok-workflow, projet : Devin ». Un contexte faux ferait proposer une
/// action sur le mauvais fichier.
/// </summary>
public class WorkContextTests
{
    private static GetWorkContextTool Tool() => new(new Mock<IDaemonClient>().Object);

    private static string Payload(string title, string app) =>
        $"{{\"active\":true,\"application\":\"{app}\",\"windowTitle\":\"{title}\"}}";

    [Theory]
    // Devin place l application AU MILIEU et le fichier en dernier.
    [InlineData("tiktok-workflow - Devin - .env.cloudflare", "Devin", ".env.cloudflare")]
    // Notepad : fichier puis nom affiche de l application, qui differe du nom de processus.
    [InlineData("supabase-shiftstar-dev.env - Bloc-notes", "Notepad", "supabase-shiftstar-dev.env")]
    // VS Code : fichier, projet, application.
    [InlineData("useVAD.ts - ShiftCore - Visual Studio Code", "Code", "useVAD.ts")]
    // Pastille de modification non enregistree.
    [InlineData("● Program.cs - Orion.Api - Visual Studio", "devenv", "Program.cs")]
    public void Le_fichier_est_reconnu_quelle_que_soit_sa_position(string title, string app, string attendu)
    {
        var card = Tool().BuildCard(ToolResult.SuccessResult(Payload(title, app)));

        Assert.NotNull(card);
        Assert.Equal(attendu, card!.Value);
        Assert.Equal(HudCardLifetime.Pinned, card.Lifetime);   // widget permanent
    }

    [Theory]
    // Aucun segment ne porte d extension plausible : on ne doit RIEN inventer.
    [InlineData("ORION et 29 pages de plus - Personnel - Microsoft Edge", "msedge")]
    [InlineData("Parametres", "ApplicationFrameHost")]
    public void Sans_fichier_identifiable_on_retombe_sur_l_application(string title, string app)
    {
        var card = Tool().BuildCard(ToolResult.SuccessResult(Payload(title, app)));

        Assert.NotNull(card);
        Assert.Equal(app, card!.Value);   // pas un segment choisi au hasard
    }

    [Fact]
    public void Session_verrouillee_produit_une_carte_explicite_et_non_une_absence()
    {
        var card = Tool().BuildCard(ToolResult.SuccessResult("{\"active\":false}"));

        Assert.NotNull(card);
        Assert.Equal("session inactive", card!.Value);
    }

    [Fact]
    public void Donnee_illisible_ne_produit_AUCUNE_carte()
    {
        Assert.Null(Tool().BuildCard(ToolResult.SuccessResult("pas du json")));
    }
}