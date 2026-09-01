using System.Net.WebSockets;
using Microsoft.Extensions.Logging;
using Moq;
using Orion.Business.Daemon;

namespace Orion.Tests.Daemon;

/// <summary>
/// Le daemon redemarre plus vite que le backend ne detecte sa mort.
///
/// Constate le 2026-09-01 : une reinstallation du daemon laissait ORION annoncer « PC eteint »
/// alors qu'il etait connecte, et l'etat restait faux INDEFINIMENT — le nouveau daemon n'avait
/// aucune raison de se reconnecter, sa socket allait tres bien.
///
/// La cause : le nettoyage retirait la connexion PAR CLE. Le remplacant s'etait deja enregistre
/// sous la meme cle quand l'ancienne boucle s'est apercue de la mort de SA socket.
/// </summary>
public class DaemonReconnectRaceTests
{
    /// <summary>Socket dont on controle le moment exact de la fermeture.</summary>
    private sealed class FakeWebSocket : WebSocket
    {
        private readonly TaskCompletionSource _close = new();

        public void Fermer() => _close.TrySetResult();

        public override WebSocketState State { get; } = WebSocketState.Open;

        public override async Task<WebSocketReceiveResult> ReceiveAsync(
            ArraySegment<byte> buffer, CancellationToken cancellationToken)
        {
            await _close.Task.WaitAsync(cancellationToken);
            return new WebSocketReceiveResult(0, WebSocketMessageType.Close, true);
        }

        public override WebSocketCloseStatus? CloseStatus => null;
        public override string? CloseStatusDescription => null;
        public override string? SubProtocol => null;
        public override void Abort() { }
        public override Task CloseAsync(WebSocketCloseStatus s, string? d, CancellationToken ct) => Task.CompletedTask;
        public override Task CloseOutputAsync(WebSocketCloseStatus s, string? d, CancellationToken ct) => Task.CompletedTask;
        public override void Dispose() { }
        public override Task SendAsync(ArraySegment<byte> b, WebSocketMessageType t, bool e, CancellationToken ct)
            => Task.CompletedTask;
    }

    [Fact]
    public async Task OldConnectionDying_DoesNotUnregisterItsReplacement()
    {
        var client = new DaemonWebSocketClient(Mock.Of<ILogger<DaemonWebSocketClient>>());

        var ancienne = new FakeWebSocket();
        var nouvelle = new FakeWebSocket();

        // 1. Le daemon est connecte.
        var boucleAncienne = client.RegisterConnectionAsync("HexaNexus", ancienne);
        Assert.True(client.IsConnected);

        // 2. Il est tue et redemarre : il se reenregistre sous la MEME cle avant que le backend
        //    n'ait detecte la mort de l'ancienne socket.
        var boucleNouvelle = client.RegisterConnectionAsync("HexaNexus", nouvelle);

        // 3. L'ancienne boucle s'apercoit enfin de la mort et fait son menage.
        ancienne.Fermer();
        await boucleAncienne;

        // 4. LE point du test : la connexion VIVANTE doit avoir survecu au menage de la morte.
        Assert.True(client.IsConnected,
            "le nettoyage de l'ancienne connexion a desinscrit la nouvelle — ORION dira « PC eteint »");

        nouvelle.Fermer();
        await boucleNouvelle;
    }

    [Fact]
    public async Task LastConnectionClosing_LeavesTheDaemonDisconnected()
    {
        // La contrepartie : sans elle, un nettoyage qui ne retirerait JAMAIS rien passerait le
        // test precedent tout en laissant croire eternellement que le PC est allume.
        var client = new DaemonWebSocketClient(Mock.Of<ILogger<DaemonWebSocketClient>>());
        var socket = new FakeWebSocket();

        var boucle = client.RegisterConnectionAsync("HexaNexus", socket);
        Assert.True(client.IsConnected);

        socket.Fermer();
        await boucle;

        Assert.False(client.IsConnected);
    }
}
