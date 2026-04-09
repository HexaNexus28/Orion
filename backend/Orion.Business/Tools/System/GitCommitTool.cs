using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.Extensions.Logging;
using Orion.Core.DTOs.Requests;
using Orion.Core.DTOs.Responses;
using Orion.Core.Interfaces.Daemon;
using Orion.Core.Interfaces.Tools;

namespace Orion.Business.Tools.System;

public class GitCommitTool : ITool
{
    private readonly IDaemonClient _daemon;
    private readonly ILogger<GitCommitTool> _logger;

    public GitCommitTool(IDaemonClient daemon, ILogger<GitCommitTool> logger)
    {
        _daemon = daemon;
        _logger = logger;
    }

    public string Name => "git_commit";
    public string Description => "Effectue un commit git rapide avec un message depuis ORION";

    public JsonObject InputSchema => new()
    {
        ["type"] = "object",
        ["properties"] = new JsonObject
        {
            ["message"] = new JsonObject
            {
                ["type"] = "string",
                ["description"] = "Message du commit git"
            },
            ["path"] = new JsonObject
            {
                ["type"] = "string",
                ["description"] = "Chemin du dépôt git (optionnel, défaut: répertoire courant)"
            }
        },
        ["required"] = new JsonArray { "message" }
    };

    public async Task<ApiResponse<ToolResult>> ExecuteAsync(JsonObject input, CancellationToken ct = default)
    {
        if (!_daemon.IsConnected)
        {
            _logger.LogWarning("[GitCommitTool] Daemon non connecté");
            return ApiResponse<ToolResult>.ErrorResponse("Daemon non connecté", 503);
        }

        var message = input["message"]?.GetValue<string>();
        if (string.IsNullOrWhiteSpace(message))
        {
            return ApiResponse<ToolResult>.ErrorResponse("Paramètre message requis", 400);
        }

        var path = input["path"]?.GetValue<string>() ?? ".";

        var request = new DaemonActionRequest
        {
            RequestId = Guid.NewGuid().ToString("N"),
            Action = "git_commit",
            Payload = new { message, path }
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
