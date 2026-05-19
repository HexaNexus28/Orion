using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.Extensions.Logging;
using Orion.Core.DTOs.Requests;
using Orion.Core.DTOs.Responses;
using Orion.Core.Interfaces.Daemon;
using Orion.Core.Interfaces.Tools;

namespace Orion.Business.Tools.System;

public class RunScriptTool : ITool
{
    private readonly IDaemonClient _daemon;
    private readonly ILogger<RunScriptTool> _logger;

    public RunScriptTool(IDaemonClient daemon, ILogger<RunScriptTool> logger)
    {
        _daemon = daemon;
        _logger = logger;
    }

    public string Name => "run_script";
    public string Description => "Exécute un script PowerShell sur le PC Windows et retourne la sortie";

    public JsonObject InputSchema => new()
    {
        ["type"] = "object",
        ["properties"] = new JsonObject
        {
            ["script"] = new JsonObject { ["type"] = "string", ["description"] = "Script PowerShell à exécuter" },
            ["workingDir"] = new JsonObject { ["type"] = "string", ["description"] = "Répertoire de travail (optionnel)" }
        },
        ["required"] = new JsonArray { "script" }
    };

    public async Task<ApiResponse<ToolResult>> ExecuteAsync(JsonObject input, CancellationToken ct = default)
    {
        if (!_daemon.IsConnected)
            return ApiResponse<ToolResult>.ErrorResponse("Daemon non connecté", 503);

        var script = input["script"]?.GetValue<string>();
        if (string.IsNullOrWhiteSpace(script))
            return ApiResponse<ToolResult>.ErrorResponse("Paramètre script requis", 400);

        var workingDir = input["workingDir"]?.GetValue<string>();

        var result = await _daemon.SendActionAsync(new DaemonActionRequest
        {
            RequestId = Guid.NewGuid().ToString("N"),
            Action = "run_script",
            Payload = new { script, workingDir }
        }, ct);

        if (!result.Success)
            return ApiResponse<ToolResult>.ErrorResponse(result.Message ?? "Daemon error", result.StatusCode);

        return ApiResponse<ToolResult>.SuccessResponse(ToolResult.SuccessResult(
            JsonSerializer.Serialize(result.Data?.Data), Name));
    }
}
