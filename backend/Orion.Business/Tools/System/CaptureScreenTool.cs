using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.Extensions.Logging;
using Orion.Core.DTOs.Requests;
using Orion.Core.DTOs.Responses;
using Orion.Core.Interfaces.Daemon;
using Orion.Core.Interfaces.Tools;

namespace Orion.Business.Tools.System;

public class CaptureScreenTool : ITool
{
    private readonly IDaemonClient _daemon;
    private readonly ILogger<CaptureScreenTool> _logger;

    public CaptureScreenTool(IDaemonClient daemon, ILogger<CaptureScreenTool> logger)
    {
        _daemon = daemon;
        _logger = logger;
    }

    public string Name => "capture_screen";
    public string Description => "Capture une screenshot de l'écran Windows complet et retourne l'image en base64";

    public bool RequiresDaemon => true;

    public JsonObject InputSchema => new()
    {
        ["type"] = "object",
        ["properties"] = new JsonObject
        {
            ["savePath"] = new JsonObject { ["type"] = "string", ["description"] = "Chemin optionnel pour sauvegarder l'image en plus" }
        }
    };

    public async Task<ApiResponse<ToolResult>> ExecuteAsync(JsonObject input, CancellationToken ct = default)
    {
        if (!_daemon.IsConnected)
            return ApiResponse<ToolResult>.ErrorResponse("Daemon non connecté", 503);

        var savePath = input["savePath"]?.GetValue<string>();

        var result = await _daemon.SendActionAsync(new DaemonActionRequest
        {
            RequestId = Guid.NewGuid().ToString("N"),
            Action = "capture_screen",
            Payload = new { savePath }
        }, ct);

        if (!result.Success)
            return ApiResponse<ToolResult>.ErrorResponse(result.Message ?? "Daemon error", result.StatusCode);

        return ApiResponse<ToolResult>.SuccessResponse(ToolResult.SuccessResult(
            JsonSerializer.Serialize(result.Data?.Data), Name));
    }
}
