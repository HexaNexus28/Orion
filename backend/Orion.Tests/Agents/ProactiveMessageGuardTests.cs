using Orion.Business.Agents;

namespace Orion.Tests.Agents;

/// <summary>
/// Le chemin proactif n'a AUCUN outil branche : le daemon signale, il n'agit pas.
///
/// Le prompt interdit deja de promettre une action, avec des exemples explicites. Le modele l'a
/// fait quand meme, en production le 2026-09-01 : « Je redemarre le service et verifie les logs,
/// attends. » L'utilisateur a attendu un travail qui n'aurait jamais lieu.
///
/// C'est la demonstration d'ADR-015 : un garde-fou dans le prompt n'est pas un garde-fou. Ces
/// tests verrouillent celui qui est dans le CODE.
/// </summary>
public class ProactiveMessageGuardTests
{
    [Theory]
    [InlineData("Je redémarre le service et vérifie les logs, attends.")]
    [InlineData("La connexion est instable, je vais regarder ça.")]
    [InlineData("Je m'en occupe tout de suite.")]
    [InlineData("J'optimise les processus en arrière-plan.")]
    [InlineData("Laisse-moi corriger ça.")]
    [InlineData("Un instant, je relance le conteneur.")]
    [InlineData("Je te tiens au courant.")]
    public void PromisesAction_ActionPromised_IsRefused(string message)
    {
        Assert.True(BriefingAgent.PromisesAction(message), $"non detecte : « {message} »");
    }

    [Theory]
    [InlineData("Ton CPU tourne à 97 % depuis deux minutes.")]
    [InlineData("Le backend ne répond plus depuis cinq minutes.")]
    [InlineData("Trois heures de travail continu, une pause serait raisonnable.")]
    [InlineData("Le dépôt ShiftCore a douze commits non poussés.")]
    [InlineData("Il est 23 h passées.")]
    public void PromisesAction_PlainStatement_IsAccepted(string message)
    {
        // La contrepartie, sans laquelle un filtre qui refuserait TOUT serait vert : ces
        // messages sont exactement ce que la proactivite doit dire.
        Assert.False(BriefingAgent.PromisesAction(message), $"faux positif : « {message} »");
    }

    [Fact]
    public void PromisesAction_IgnoresCase()
    {
        Assert.True(BriefingAgent.PromisesAction("JE REDEMARRE LE SERVICE"));
    }

    [Fact]
    public void PromisesAction_WorksWithoutAccents()
    {
        // Le modele ecrit parfois sans accents. Refuser « je verifie » et accepter « je vérifie »
        // laisserait la moitie des promesses passer.
        Assert.True(BriefingAgent.PromisesAction("Je verifie les logs."));
        Assert.True(BriefingAgent.PromisesAction("Je vérifie les logs."));
    }
}
