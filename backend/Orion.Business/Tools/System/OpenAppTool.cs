using System.Text.Json;
using System.Text.Json.Nodes;
using Orion.Core.DTOs.Requests;
using Orion.Core.DTOs.Responses;
using Orion.Core.Interfaces.Daemon;
using Orion.Core.Interfaces.Tools;

namespace Orion.Business.Tools.System;

public class OpenAppTool : ITool
{
    private readonly IDaemonClient _daemon;

    public OpenAppTool(IDaemonClient daemon)
    {
        _daemon = daemon;
    }

    public string Name => "open_app";
    public string Description => "Ouvre une application sur le PC Windows (whitelist sécurisée)";

    public bool RequiresDaemon => true;

    /// <summary>Lancer une application attend très bien le réveil du PC.</summary>
    public bool IsDeferrable => true;

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

        var appName = input["appName"]?.GetValue<string>();
        if (string.IsNullOrWhiteSpace(appName))
        {
            return ApiResponse<ToolResult>.ErrorResponse("Paramètre appName requis", 400);
        }

        var request = new DaemonActionRequest
        {
            RequestId = Guid.NewGuid().ToString("N"),
            Action = "open_app",
            Payload = new { application = appName }
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
