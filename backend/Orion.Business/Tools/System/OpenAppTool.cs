using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.Extensions.Logging;
using Orion.Core.DTOs.Requests;
using Orion.Core.DTOs.Responses;
using Orion.Core.Interfaces.Daemon;
using Orion.Core.Interfaces.Tools;

namespace Orion.Business.Tools.System;

public class OpenAppTool : ITool
{
    private readonly IDaemonClient _daemon;
    private readonly ILogger<OpenAppTool> _logger;

    public OpenAppTool(IDaemonClient daemon, ILogger<OpenAppTool> logger)
    {
        _daemon = daemon;
        _logger = logger;
    }

    public string Name => "open_app";
    public string Description => "Ouvre une application sur le PC Windows (whitelist sécurisée)";

    public JsonObject InputSchema => new()
    {
        ["type"] = "object",
        ["properties"] = new JsonObject
        {
            ["appName"] = new JsonObject
            {
                ["type"] = "string",
                ["description"] = "Nom de l'application à ouvrir (ex: notepad, vscode, chrome)"
            }
        },
        ["required"] = new JsonArray { "appName" }
    };

    public async Task<ApiResponse<ToolResult>> ExecuteAsync(JsonObject input, CancellationToken ct = default)
    {
        if (!_daemon.IsConnected)
        {
            _logger.LogWarning("[OpenAppTool] Daemon non connecté");
            return ApiResponse<ToolResult>.ErrorResponse("Daemon non connecté", 503);
        }

        var appName = input["appName"]?.GetValue<string>();
        if (string.IsNullOrWhiteSpace(appName))
        {
            return ApiResponse<ToolResult>.ErrorResponse("Paramètre appName requis", 400);
        }

        var request = new DaemonActionRequest
        {
            RequestId = Guid.NewGuid().ToString("N"),
            Action = "open_app",
            Payload = new { appName }
        };

        var result = await _daemon.SendActionAsync(request, ct);

        if (!result.Success)
        {
            return ApiResponse<ToolResult>.ErrorResponse(result.Message ?? "Daemon error", result.StatusCode);
        }

        return ApiResponse<ToolResult>.SuccessResponse(ToolResult.SuccessResult(
            JsonSerializer.Serialize(result.Data?.Data),
            Name));
    }
}
