using System.Text.Json;
using System.Text.Json.Nodes;
using Orion.Core.DTOs.Requests;
using Orion.Core.DTOs.Responses;
using Orion.Core.Interfaces.Daemon;
using Orion.Core.Interfaces.Tools;

namespace Orion.Business.Tools.System;

public class TypeTextTool : ITool
{
    private readonly IDaemonClient _daemon;

    public TypeTextTool(IDaemonClient daemon)
    {
        _daemon = daemon;
    }

    public string Name => "type_text";
    public string Description => "Simule la frappe clavier dans la fenêtre active sur le PC Windows";

    public bool RequiresDaemon => true;
    public bool IsDestructive => true;

    public JsonObject InputSchema => new()
    {
        ["type"] = "object",
        ["properties"] = new JsonObject
        {
            ["text"] = new JsonObject { ["type"] = "string", ["description"] = "Texte à taper dans la fenêtre active" },
            ["delayMs"] = new JsonObject { ["type"] = "integer", ["description"] = "Délai avant de taper en ms (défaut: 500)" }
        },
        ["required"] = new JsonArray { "text" }
    };

    public async Task<ApiResponse<ToolResult>> ExecuteAsync(JsonObject input, CancellationToken ct = default)
    {

        var text = input["text"]?.GetValue<string>();
        if (string.IsNullOrEmpty(text))
            return ApiResponse<ToolResult>.ErrorResponse("Paramètre text requis", 400);

        var delayMs = input["delayMs"]?.GetValue<int>() ?? 500;

        var result = await _daemon.SendActionAsync(new DaemonActionRequest
        {
            RequestId = Guid.NewGuid().ToString("N"),
            Action = "type_text",
            Payload = new { text, delayMs }
        }, ct);

        if (!result.Success)
            return ApiResponse<ToolResult>.ErrorResponse(result.Message ?? "Daemon error", result.StatusCode);

        return ApiResponse<ToolResult>.SuccessResponse(ToolResult.SuccessResult(
            JsonSerializer.Serialize(result.Data?.Data), Name));
    }
}
