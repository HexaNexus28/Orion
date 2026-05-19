namespace Orion.Api.WebSockets;

/// <summary>
/// Middleware that accepts WebSocket connections on /ws/voice
/// and delegates to VoiceWebSocketHandler for full-duplex voice conversation.
/// </summary>
public class VoiceWebSocketMiddleware
{
    private readonly RequestDelegate _next;
    private readonly ILogger<VoiceWebSocketMiddleware> _logger;

    public VoiceWebSocketMiddleware(RequestDelegate next, ILogger<VoiceWebSocketMiddleware> logger)
    {
        _next = next;
        _logger = logger;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        if (context.Request.Path == "/ws/voice")
        {
            if (!context.WebSockets.IsWebSocketRequest)
            {
                context.Response.StatusCode = 400;
                await context.Response.WriteAsync("WebSocket connection expected");
                return;
            }

            using var webSocket = await context.WebSockets.AcceptWebSocketAsync();
            
            // Create a scoped handler per connection
            var handler = new VoiceWebSocketHandler(
                context.RequestServices.GetRequiredService<ILogger<VoiceWebSocketHandler>>(),
                context.RequestServices.GetRequiredService<IServiceScopeFactory>(),
                context.RequestServices.GetRequiredService<Orion.Core.Interfaces.Services.IWhisperService>()
            );

            await handler.HandleAsync(webSocket, context.RequestAborted);
        }
        else
        {
            await _next(context);
        }
    }
}
