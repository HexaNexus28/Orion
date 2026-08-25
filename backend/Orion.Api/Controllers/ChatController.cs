using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Mvc;
using Orion.Core.DTOs.Requests;
using Orion.Core.DTOs.Responses;
using Orion.Core.Interfaces.Services;

namespace Orion.Api.Controllers;

[ApiController]
[Route("api/[controller]")]
public class ChatController : ControllerBase
{
    private readonly IChatService _chatService;

    public ChatController(IChatService chatService)
    {
        _chatService = chatService;
    }

    [HttpPost]
    public async Task<IActionResult> Chat([FromBody] ChatRequest request, CancellationToken ct)
    {
        var response = await _chatService.SendMessageAsync(request, ct);
        return StatusCode(response.StatusCode, response);
    }

    /// <summary>
    /// Flux SSE d'événements agent typés : token, tool_start, tool_result, done, error.
    /// Chaque événement est un objet JSON sur une seule ligne — l'ancien format « texte brut »
    /// cassait le cadrage SSE dès qu'un token contenait un retour à la ligne.
    /// </summary>
    [HttpPost("stream")]
    public async Task StreamChat([FromBody] ChatRequest request, CancellationToken ct)
    {
        Response.ContentType = "text/event-stream; charset=utf-8";
        Response.Headers["Cache-Control"] = "no-cache";
        Response.Headers["X-Accel-Buffering"] = "no";

        await foreach (var evt in _chatService.StreamMessageAsync(request, ct))
        {
            var payload = JsonSerializer.Serialize(new
            {
                type = WireName(evt.Type),
                text = evt.Text,
                tool = evt.ToolName,
                args = evt.ToolArgs,
                ok = evt.ToolOk,
                summary = evt.ToolSummary,
                iteration = evt.Iteration
            }, SseJson);

            await Response.WriteAsync($"data: {payload}\n\n", ct);
            await Response.Body.FlushAsync(ct);
        }

        await Response.WriteAsync("data: [DONE]\n\n", ct);
        await Response.Body.FlushAsync(ct);
    }

    private static readonly JsonSerializerOptions SseJson = new()
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    private static string WireName(AgentEventType type) => type switch
    {
        AgentEventType.Token => "token",
        AgentEventType.ToolStart => "tool_start",
        AgentEventType.ToolResult => "tool_result",
        AgentEventType.Done => "done",
        AgentEventType.Error => "error",
        _ => "unknown"
    };

    [HttpGet("{sessionId}")]
    public async Task<IActionResult> GetConversation(Guid sessionId, CancellationToken ct)
    {
        var response = await _chatService.GetConversationAsync(sessionId, ct);
        return StatusCode(response.StatusCode, response);
    }

    [HttpGet("history")]
    public async Task<IActionResult> GetConversations([FromQuery] int page = 1, [FromQuery] int pageSize = 20, CancellationToken ct = default)
    {
        var response = await _chatService.GetConversationsAsync(page, pageSize, ct);
        return StatusCode(response.StatusCode, response);
    }
}
