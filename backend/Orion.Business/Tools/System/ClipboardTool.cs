using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.Extensions.Logging;
using Orion.Core.DTOs.Requests;
using Orion.Core.DTOs.Responses;
using Orion.Core.Interfaces.Daemon;
using Orion.Core.Interfaces.Tools;

namespace Orion.Business.Tools.System;

public class ClipboardTool : ITool
{
    private readonly IDaemonClient _daemon;
    private readonly ILogger<ClipboardTool> _logger;

    public ClipboardTool(IDaemonClient daemon, ILogger<ClipboardTool> logger)
    {
        _daemon = daemon;
        _logger = logger;
    }

    public string Name => "clipboard";
    public string Description => "Lit ou écrit le presse-papiers Windows. Action 'get' retourne le contenu, 'set' l'écrase";

    public bool RequiresDaemon => true;
    public bool IsDestructive => true;

    public JsonObject InputSchema => new()
    {
        ["type"] = "object",
        ["properties"] = new JsonObject
        {
            ["action"] = new JsonObject
            {
                ["type"] = "string",
                ["enum"] = new JsonArray { "get", "set" },
                ["description"] = "get pour lire, set pour écrire"
            },
            ["text"] = new JsonObject { ["type"] = "string", ["description"] = "Texte à copier (requis si action=set)" }
        },
        ["required"] = new JsonArray { "action" }
    };

    public async Task<ApiResponse<ToolResult>> ExecuteAsync(JsonObject input, CancellationToken ct = default)
    {
        if (!_daemon.IsConnected)
            return ApiResponse<ToolResult>.ErrorResponse("Daemon non connecté", 503);

        var action = input["action"]?.GetValue<string>();
        if (action != "get" && action != "set")
            return ApiResponse<ToolResult>.ErrorResponse("action doit être 'get' ou 'set'", 400);

        if (action == "set" && string.IsNullOrEmpty(input["text"]?.GetValue<string>()))
            return ApiResponse<ToolResult>.ErrorResponse("Paramètre text requis pour action=set", 400);

        var daemonAction = action == "get" ? "get_clipboard" : "set_clipboard";
        var text = input["text"]?.GetValue<string>() ?? "";

        object daemonPayload = action == "get" ? new { } : new { text };

        var result = await _daemon.SendActionAsync(new DaemonActionRequest
        {
            RequestId = Guid.NewGuid().ToString("N"),
            Action = daemonAction,
            Payload = daemonPayload
        }, ct);

        if (!result.Success)
            return ApiResponse<ToolResult>.ErrorResponse(result.Message ?? "Daemon error", result.StatusCode);

        return ApiResponse<ToolResult>.SuccessResponse(ToolResult.SuccessResult(
            JsonSerializer.Serialize(result.Data?.Data), Name));
    }
}
