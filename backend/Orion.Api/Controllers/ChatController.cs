using Microsoft.AspNetCore.Mvc;
using Orion.Core.DTOs.Requests;
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

    [HttpPost("stream")]
    public async Task StreamChat([FromBody] ChatRequest request, CancellationToken ct)
    {
        Response.ContentType = "text/event-stream; charset=utf-8";
        Response.Headers["Cache-Control"] = "no-cache";
        Response.Headers["X-Accel-Buffering"] = "no";

        await foreach (var chunk in _chatService.StreamMessageAsync(request, ct))
        {
            await Response.WriteAsync($"data: {chunk}\n\n", ct);
            await Response.Body.FlushAsync(ct);
        }
        await Response.WriteAsync("data: [DONE]\n\n", ct);
        await Response.Body.FlushAsync(ct);
    }

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
