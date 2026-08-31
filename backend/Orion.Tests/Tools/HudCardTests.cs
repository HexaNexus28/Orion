using System.Text.Json.Nodes;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Orion.Business.Tools.Internet;
using Orion.Business.Tools.System;
using Orion.Core.DTOs.Responses;
using Orion.Core.Interfaces.Daemon;

namespace Orion.Tests.Tools;

/// <summary>
/// Garde-fous des cartes du HUD.
///
/// Ces tests existent parce que le mecanisme precedent fabriquait les cartes par EXPRESSION
/// REGULIERE sur la prose : toute statistique en gras en devenait une, par accident, et rien
/// n apparaissait quand ca comptait. Ils verifient que la carte reflete desormais la DONNEE
/// REELLE de l outil — et qu elle n apparait pas quand la donnee est absente ou illisible.
/// </summary>
public class HudCardTests
{
    private static GetSystemStatusTool Systeme()
        => new(new Mock<IDaemonClient>().Object);

    [Fact]
    public void HostStatus_RealData_ProducesCard()
    {
        var charge = "{\"machineName\":\"HexaNexus\",\"processorCount\":8,\"workingSetMb\":127,\"uptimeMinutes\":185,\"localTime\":\"2026-08-26 09:12:00\"}";

        var carte = Systeme().BuildCard(ToolResult.SuccessResult(charge));

        Assert.NotNull(carte);
        Assert.Equal("system.host", carte!.Id);          // identifiant STABLE : rappeler l outil met a jour
        Assert.Equal("HexaNexus", carte.Label);
        Assert.Equal("3 h 05", carte.Value);              // 185 min lisibles, pas "185"
        Assert.Equal(HudCardState.Ok, carte.State);
        Assert.NotNull(carte.Items);

        // La memoire affichee est celle du DAEMON, jamais etiquetee "RAM" : sur une machine de
        // 32 Go, afficher 127 Mo sous ce libelle serait une carte fausse.
        Assert.Contains(carte.Items!, i => i.Label == "Memoire ORION" && i.Value == "127 Mo");
        Assert.DoesNotContain(carte.Items!, i => i.Label.Contains("RAM"));
    }

    [Fact]
    public void Card_UnreadableData_NotProduced()
    {
        // Une carte vide ou fabriquee serait pire que pas de carte : le HUD doit refleter ce qui
        // s est reellement passe.
        Assert.Null(Systeme().BuildCard(ToolResult.SuccessResult("pas du json")));
        Assert.Null(Systeme().BuildCard(ToolResult.SuccessResult(null)));
    }

    [Fact]
    public void GitRepo_Dirty_WarnsAndCarriesRepoName()
    {
        var charge = "{\"path\":\"C:\\\\Projets\\\\ShiftCore\",\"branch\":\"main\",\"changes\":[\" M a.cs\",\" M b.cs\"],\"hasChanges\":true}";

        var carte = new GitStatusTool(new Mock<IDaemonClient>().Object).BuildCard(ToolResult.SuccessResult(charge));

        Assert.NotNull(carte);
        // Identifiant porte le DEPOT, pas l outil : deux depots = deux cartes, pas un ecrasement.
        Assert.Equal("git.ShiftCore", carte!.Id);
        Assert.Equal("main", carte.Value);
        Assert.Equal(HudCardState.Warn, carte.State);
        Assert.Equal(2, carte.Items!.Count);
    }

    [Fact]
    public void GitRepo_Clean_StaysNormalState()
    {
        var charge = "{\"path\":\"C:\\\\Projets\\\\Orion\",\"branch\":\"main\",\"changes\":[],\"hasChanges\":false}";

        var carte = new GitStatusTool(new Mock<IDaemonClient>().Object).BuildCard(ToolResult.SuccessResult(charge));

        Assert.Equal(HudCardState.Ok, carte!.State);
        Assert.Equal("propre", carte.Unit);
        Assert.Null(carte.Items);
    }
}