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

            // Validate token
            var token = context.Request.Headers["X-Daemon-Token"].FirstOrDefault();
            var expectedToken = Environment.GetEnvironmentVariable("DAEMON_WS_TOKEN");

            if (!string.IsNullOrEmpty(expectedToken) && token != expectedToken)
            {
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
