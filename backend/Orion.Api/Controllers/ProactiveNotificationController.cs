using System.Collections.Concurrent;
using Microsoft.AspNetCore.Mvc;
using Orion.Core.DTOs.Responses;

namespace Orion.Api.Controllers;

/// <summary>
/// ProactiveNotificationController - Pont entre Daemon et Frontend
/// Le daemon envoie des notifications, le frontend les reçoit via SSE
/// </summary>
[ApiController]
[Route("api/[controller]")]
public class ProactiveNotificationController : ControllerBase
{
    private static readonly ConcurrentDictionary<string, HttpResponse> _clients = new();
    private readonly ILogger<ProactiveNotificationController> _logger;

    public ProactiveNotificationController(ILogger<ProactiveNotificationController> logger)
    {
        _logger = logger;
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

        var clientId = Guid.NewGuid().ToString();
        _clients.TryAdd(clientId, Response);
        _logger.LogInformation("[NotificationStream] Client {ClientId} connected", clientId);

        try
        {
            // Send initial connection message
            await SendEventAsync(Response, "connected", new { clientId, timestamp = DateTime.UtcNow });

            // Keep connection alive
            while (!ct.IsCancellationRequested)
            {
                await Task.Delay(30000, ct); // Heartbeat every 30s
                await SendEventAsync(Response, "heartbeat", new { timestamp = DateTime.UtcNow });
            }
        }
        catch (OperationCanceledException)
        {
            _logger.LogInformation("[NotificationStream] Client {ClientId} disconnected", clientId);
        }
        finally
        {
            _clients.TryRemove(clientId, out _);
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

        var deadClients = new List<string>();

        foreach (var (clientId, response) in _clients)
        {
            try
            {
                await SendEventAsync(response, "notification", notification);
            }
            catch
            {
                deadClients.Add(clientId);
            }
        }

        // Cleanup dead clients
        foreach (var clientId in deadClients)
        {
            _clients.TryRemove(clientId, out _);
        }

        return Ok(ApiResponse<object>.SuccessResponse(new { clientsNotified = _clients.Count }));
    }

    /// <summary>
    /// Frontend peut demander une action au daemon via le backend
    /// </summary>
    [HttpPost("action")]
    public async Task<IActionResult> SendActionToDaemon([FromBody] FrontendActionRequest request)
    {
        // Cette action sera forwardée au daemon via WebSocket
        _logger.LogInformation("[Action] Frontend requested: {Action}", request.Action);
        
        // TODO: Implémenter le forward au daemon via IDaemonClient
        
        return Ok(ApiResponse<object>.SuccessResponse(new { forwarded = true }));
    }

    private static async Task SendEventAsync(HttpResponse response, string eventName, object data)
    {
        var json = System.Text.Json.JsonSerializer.Serialize(data);
        await response.WriteAsync($"event: {eventName}\n");
        await response.WriteAsync($"data: {json}\n\n");
        await response.Body.FlushAsync();
    }
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
