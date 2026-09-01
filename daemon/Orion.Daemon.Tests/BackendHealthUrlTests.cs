using Orion.Daemon.Core.Configuration;

namespace Orion.Daemon.Tests;

/// <summary>
/// L'URL de sante du backend se DEDUIT de celle a laquelle le daemon parle.
///
/// La configuration livree sondait \\localhost:5107 — l'adresse du backend en DEVELOPPEMENT —
/// pendant que le daemon etait connecte au VPS. Rien n'ecoutait la, la sonde echouait a chaque
/// ronde, et `service_down` (urgence 90, seuil d'interruption 55) annoncait en boucle un backend
/// mort alors que le WebSocket fonctionnait au meme instant.
///
/// Deux adresses pour un seul backend finissent toujours par diverger. Ces tests verrouillent
/// le fait qu'il n'y en ait plus qu'une.
/// </summary>
public class BackendHealthUrlTests
{
    [Theory]
    [InlineData("wss://orion.shift-star.app/daemon", "https://orion.shift-star.app/api/health")]
    [InlineData("ws://localhost:5107/daemon", "http://localhost:5107/api/health")]
    [InlineData("wss://orion.shift-star.app:8443/daemon", "https://orion.shift-star.app:8443/api/health")]
    public void BackendHealthUrl_DerivedFromWebSocketUrl(string ws, string attendu)
    {
        var options = new DaemonOptions { RenderWsUrl = ws };

        Assert.Equal(attendu, options.BackendHealthUrl);
    }

    [Fact]
    public void BackendHealthUrl_FollowsTheScheme()
    {
        // `wss` -> `https`, jamais l'inverse : sonder en clair un backend joint en TLS ferait
        // echouer la sonde sur un backend parfaitement vivant.
        Assert.StartsWith("https://", new DaemonOptions { RenderWsUrl = "wss://x.tld/daemon" }.BackendHealthUrl);
        Assert.StartsWith("http://", new DaemonOptions { RenderWsUrl = "ws://x.tld/daemon" }.BackendHealthUrl);
    }

    [Theory]
    [InlineData("")]
    [InlineData("pas-une-url")]
    public void BackendHealthUrl_UnusableWsUrl_ReturnsEmpty(string ws)
    {
        // Vide = le watcher ne sonde rien, plutot que de sonder une adresse inventee et
        // d'annoncer un backend mort. Une sonde impossible doit se taire, pas alerter.
        Assert.Equal(string.Empty, new DaemonOptions { RenderWsUrl = ws }.BackendHealthUrl);
    }
}
