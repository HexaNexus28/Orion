using Orion.Api.Authentication;
using Orion.Core.Interfaces.Daemon;

namespace Orion.Api.Middleware;

public class DaemonWebSocketMiddleware
{
    private readonly RequestDelegate _next;
    private readonly ILogger<DaemonWebSocketMiddleware> _logger;
    private readonly IDaemonClient _daemonClient;

    public DaemonWebSocketMiddleware(
        RequestDelegate next,
        ILogger<DaemonWebSocketMiddleware> logger,
        IDaemonClient daemonClient)
    {
        _next = next;
        _logger = logger;
        _daemonClient = daemonClient;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        if (context.Request.Path != "/daemon")
        {
            await _next(context);
            return;
        }

        if (!context.WebSockets.IsWebSocketRequest)
        {
            context.Response.StatusCode = 400;
            return;
        }

        // La comparaison du jeton ne se fait PLUS ici. Elle appartient au schema
        // d'authentification (DaemonAuthenticationHandler), qui s'applique aussi aux appels HTTP
        // du daemon. Tant que ce controle vivait dans ce seul middleware, chaque nouveau canal
        // repartait sans protection — c'est exactement ce qui est arrive a /ws/voice.
        //
        // Le role est exige explicitement : un jeton de proprietaire ne doit pas ouvrir le canal
        // qui pilote la machine, et inversement.
        if (!WebSocketAuthGuard.Require(context, OrionAuth.DaemonRole, _logger, "Daemon"))
            return;

        var machineName = context.Request.Headers["X-Machine-Name"].FirstOrDefault() ?? "unknown";

        using var webSocket = await context.WebSockets.AcceptWebSocketAsync();

        // Await the receive loop directly — no second concurrent ReceiveAsync
        // Propagate RequestAborted so the receive loop stops when the HTTP connection drops
        await _daemonClient.RegisterConnectionAsync(machineName, webSocket, context.RequestAborted);
    }
}
