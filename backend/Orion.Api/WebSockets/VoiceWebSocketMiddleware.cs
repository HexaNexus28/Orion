using Orion.Api.Authentication;

namespace Orion.Api.WebSockets;

/// <summary>
/// Accepte les connexions WebSocket sur /ws/voice et delegue a VoiceWebSocketHandler
/// pour la conversation vocale full-duplex.
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
        if (context.Request.Path != "/ws/voice")
        {
            await _next(context);
            return;
        }

        if (!context.WebSockets.IsWebSocketRequest)
        {
            context.Response.StatusCode = 400;
            await context.Response.WriteAsync("WebSocket connection expected");
            return;
        }

        // Ce canal n'avait AUCUN controle. Ce n'etait pas seulement une ecoute possible : le tour
        // vocal declenche des outils, donc des actions reelles sur la machine de l'utilisateur.
        // Connaitre l'URL suffisait a parler a l'assistant ET a le faire agir.
        //
        // Le jeton arrive par ?access_token= (un WebSocket navigateur ne peut porter aucun
        // en-tete) et a deja ete valide par le schema JWT — cf. OrionAuth.QueryTokenPaths.
        if (!WebSocketAuthGuard.Require(context, OrionAuth.OwnerRole, _logger, "Voice"))
            return;

        using var webSocket = await context.WebSockets.AcceptWebSocketAsync();

        // Un handler par connexion, dans la portee de la requete.
        var handler = new VoiceWebSocketHandler(
            context.RequestServices.GetRequiredService<ILogger<VoiceWebSocketHandler>>(),
            context.RequestServices.GetRequiredService<IServiceScopeFactory>(),
            context.RequestServices.GetRequiredService<Orion.Core.Interfaces.Services.IWhisperService>()
        );

        await handler.HandleAsync(webSocket, context.RequestAborted);
    }
}
