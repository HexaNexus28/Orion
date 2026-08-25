using System.Net;
using System.Net.WebSockets;
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
        if (context.Request.Path == "/daemon")
        {
            if (!context.WebSockets.IsWebSocketRequest)
            {
                context.Response.StatusCode = 400;
                return;
            }

            // Validation du jeton — FAIL-CLOSED.
            //
            // Avant : `if (!string.IsNullOrEmpty(expectedToken) && token != expectedToken)`.
            // Une variable d'environnement absente faisait SAUTER le controle : n'importe qui
            // pouvait ouvrir ce WebSocket. Or ce canal fait executer des actions sur la machine
            // de l'utilisateur — un oubli de configuration valait execution de code a distance.
            // Un defaut de configuration doit FERMER la porte, jamais l'ouvrir.
            var token = context.Request.Headers["X-Daemon-Token"].FirstOrDefault();
            var expectedToken = Environment.GetEnvironmentVariable("DAEMON_WS_TOKEN");

            if (string.IsNullOrEmpty(expectedToken))
            {
                _logger.LogError("[Daemon] DAEMON_WS_TOKEN non configure — connexion REFUSEE. " +
                    "Definir la variable d'environnement avant de demarrer en production.");
                context.Response.StatusCode = 503;
                return;
            }

            if (token != expectedToken)
            {
                _logger.LogWarning("[Daemon] Jeton invalide depuis {Ip}", context.Connection.RemoteIpAddress);
                context.Response.StatusCode = 401;
                return;
            }

            var machineName = context.Request.Headers["X-Machine-Name"].FirstOrDefault() ?? "unknown";

            using var webSocket = await context.WebSockets.AcceptWebSocketAsync();

            // Await the receive loop directly — no second concurrent ReceiveAsync
            // Propagate RequestAborted so the receive loop stops when the HTTP connection drops
            await _daemonClient.RegisterConnectionAsync(machineName, webSocket, context.RequestAborted);
        }
        else
        {
            await _next(context);
        }
    }


}
