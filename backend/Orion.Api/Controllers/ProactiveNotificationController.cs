using System.Collections.Concurrent;
using Microsoft.AspNetCore.Authorization;
using Orion.Api.Authentication;
using Microsoft.AspNetCore.Mvc;
using Orion.Core.DTOs.Requests;
using Orion.Core.DTOs.Responses;
using Orion.Core.Interfaces.Agents;
using Orion.Core.Interfaces.Services;
using Orion.Api.Services;
using Orion.Core.Interfaces.Daemon;

namespace Orion.Api.Controllers;

/// <summary>
/// ProactiveNotificationController - Pont entre Daemon et Frontend
/// Le daemon envoie des notifications, le frontend les reçoit via SSE
/// </summary>
[ApiController]
[Route("api/[controller]")]
public class ProactiveNotificationController : ControllerBase
{
    private readonly SseClientRegistry _sse;
    private readonly IDaemonClient _daemonClient;
    private readonly IBriefingAgent _briefingAgent;
    private readonly IProactiveLearningService _apprentissage;
    private readonly ILogger<ProactiveNotificationController> _logger;

    public ProactiveNotificationController(
        IDaemonClient daemonClient,
        IBriefingAgent briefingAgent,
        IProactiveLearningService apprentissage,
        ILogger<ProactiveNotificationController> logger,
        SseClientRegistry sse)
    {
        _sse = sse;
        _daemonClient = daemonClient;
        _briefingAgent = briefingAgent;
        _apprentissage = apprentissage;
        _logger = logger;
    }

    // Internal broadcast — used by BriefingScheduler and this controller
    internal static async Task BroadcastAsync(
        string eventType, string message, string priority, bool speak,
        ILogger logger, SseClientRegistry sse)
    {
        var notification = new DaemonNotificationDto
        {
            Type = eventType,
            Title = "ORION",
            Message = message,
            Priority = priority,
            Speak = speak,
            Timestamp = DateTime.UtcNow
        };

        var restants = await sse.BroadcastAsync("notification", notification);

        logger.LogInformation("[Broadcast] {Preview} → {Count} clients",
            message.Length > 60 ? message[..60] + "..." : message, restants);
    }

    /// <summary>
    /// Stream de notifications proactive (SSE - Server-Sent Events)
    /// Le frontend s'y connecte pour recevoir les notifications du daemon
    /// </summary>
    [HttpGet("stream")]
    public async Task StreamNotifications(CancellationToken ct)
    {
        Response.Headers.Append("Content-Type", "text/event-stream");
        Response.Headers.Append("Cache-Control", "no-cache");
        Response.Headers.Append("Connection", "keep-alive");

        // Sans cet en-tete, nginx TAMPONNE la reponse : par defaut proxy_buffering est actif,
        // donc il accumule le flux au lieu de le transmettre. Les evenements — battements de
        // coeur compris — restaient bloques dans son tampon, et les notifications proactives
        // n arrivaient jamais en temps reel. ChatController le posait deja ; celui-ci l avait
        // oublie, et le defaut ne se voit que derriere un proxy, jamais en developpement.
        Response.Headers["X-Accel-Buffering"] = "no";

        var clientId = Guid.NewGuid().ToString();
        _sse.Add(clientId, Response);
        _logger.LogInformation("[NotificationStream] Client {ClientId} connected", clientId);

        try
        {
            // Send initial connection message
            await SseClientRegistry.SendAsync(Response, "connected", new { clientId, timestamp = DateTime.UtcNow });

            // Keep connection alive
            while (!ct.IsCancellationRequested)
            {
                await Task.Delay(30000, ct); // Heartbeat every 30s
                await SseClientRegistry.SendAsync(Response, "heartbeat", new { timestamp = DateTime.UtcNow });
            }
        }
        catch (OperationCanceledException)
        {
            _logger.LogInformation("[NotificationStream] Client {ClientId} disconnected", clientId);
        }
        finally
        {
            _sse.Remove(clientId);
        }
    }

    /// <summary>
    /// Le daemon appelle ce endpoint pour envoyer une notification au frontend
    /// </summary>
    [HttpPost("notify")]
    public async Task<IActionResult> SendNotification([FromBody] DaemonNotificationDto notification)
    {
        _logger.LogInformation("[Notification] Broadcasting: {Type} - {Message}", 
            notification.Type, notification.Message);

        var clientsNotifies = await _sse.BroadcastAsync("notification", notification);

        return Ok(ApiResponse<object>.SuccessResponse(new { clientsNotified = clientsNotifies }));
    }

    /// <summary>
    /// Le daemon appelle ce endpoint avec un pattern détecté → LLM génère le message → broadcast SSE
    /// </summary>
    // Appelee par le DAEMON, jamais par le navigateur. Le role est exige explicitement :
    // sans cet attribut la politique par defaut exigerait `owner`, et le daemon — qui n.a pas
    // de JWT et ne peut pas en obtenir — se prendrait un 401 a chaque detection. C.est
    // exactement ce qui se passait : le mode proactif etait mort en silence.
    [Authorize(Policy = OrionAuth.DaemonPolicy)]
    [HttpPost("trigger")]
    public async Task<IActionResult> TriggerProactiveMessage(
        [FromBody] ProactiveTriggerRequest request, CancellationToken ct)
    {
        _logger.LogInformation("[Trigger] Pattern: {Pattern} | Context: {Context}",
            request.Pattern, request.Context);

        var messageResult = await _briefingAgent.GenerateProactiveMessageAsync(
            request.Pattern, request.Context, ct);

        var message = messageResult.Success && !string.IsNullOrWhiteSpace(messageResult.Data)
            ? messageResult.Data
            : request.Context;

        await BroadcastAsync("proactive", message, request.Priority ?? "normal", speak: true, _logger, _sse);

        // La trace de ce qui a ETE DIT : c'est elle qui alimente l'apprentissage. La table
        // `behavior_patterns` existait depuis le premier jour sans que rien n'y ecrive.
        await _apprentissage.EnregistrerSignalementAsync(request.Pattern, request.Context, message, ct);

        return Ok(ApiResponse<object>.SuccessResponse(new { message, clientsNotified = _sse.Count }));
    }

    /// <summary>
    /// Pénalités apprises par pattern. Le daemon les récupère périodiquement et les soustrait
    /// au score : un signal rejeté plusieurs fois finit sous le seuil, puis se tait.
    /// </summary>
    [Authorize(Policy = OrionAuth.DaemonPolicy)]
    [HttpGet("weights")]
    public async Task<IActionResult> GetWeights(CancellationToken ct)
    {
        var penalites = await _apprentissage.ObtenirPenalitesAsync(ct);
        return StatusCode(penalites.StatusCode, penalites);
    }

    /// <summary>
    /// Frontend peut demander une action au daemon via le backend
    /// </summary>
    [HttpPost("action")]
    public async Task<IActionResult> SendActionToDaemon([FromBody] FrontendActionRequest request, CancellationToken ct)
    {
        _logger.LogInformation("[Action] Frontend requested: {Action}", request.Action);

        if (!_daemonClient.IsConnected)
            return StatusCode(503, ApiResponse<object>.ErrorResponse("Daemon non connecté", 503));

        var daemonAction = new DaemonActionRequest
        {
            RequestId = Guid.NewGuid().ToString("N"),
            Action = request.Action,
            Payload = request.Data ?? new Dictionary<string, object>()
        };

        var result = await _daemonClient.SendActionAsync(daemonAction, ct);
        return StatusCode(result.StatusCode, result);
    }

    // La serialisation et l envoi vivent desormais dans SseClientRegistry : ils servaient a
    // trois endroits de ce fichier ET, maintenant, au service d arriere-plan du HUD.
}

public class ProactiveTriggerRequest
{
    public string Pattern { get; set; } = "";
    public string Context { get; set; } = "";
    public string? Priority { get; set; } = "normal";
}

public class DaemonNotificationDto
{
    public string Type { get; set; } = "info"; // info, warning, alert, proactive
    public string Title { get; set; } = "";
    public string Message { get; set; } = "";
    public string Priority { get; set; } = "normal"; // low, normal, high, critical
    public bool Speak { get; set; } = false; // Si true, ORION doit parler
    public DateTime Timestamp { get; set; } = DateTime.UtcNow;
    public Dictionary<string, object>? Metadata { get; set; }
}

public class FrontendActionRequest
{
    public string Action { get; set; } = ""; // speak, notify, query_status, etc.
    public string? Parameter { get; set; }
    public Dictionary<string, object>? Data { get; set; }
}
