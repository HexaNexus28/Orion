using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.Extensions.Logging;
using Orion.Core.DTOs.Requests;
using Orion.Core.DTOs.Responses;
using Orion.Core.Interfaces.Daemon;
using Orion.Core.Interfaces.Tools;

namespace Orion.Business.Tools.System;

public class GitStatusTool : ITool
{
    private readonly IDaemonClient _daemon;
    private readonly ILogger<GitStatusTool> _logger;

    public GitStatusTool(IDaemonClient daemon, ILogger<GitStatusTool> logger)
    {
        _daemon = daemon;
        _logger = logger;
    }

    public string Name => "git_status";
    public string Description => "Retourne le statut git d'un dépôt (branche, fichiers modifiés)";

    public JsonObject InputSchema => new()
    {
        ["type"] = "object",
        ["properties"] = new JsonObject
        {
            ["path"] = new JsonObject
            {
                ["type"] = "string",
                ["description"] = "Chemin du dépôt git (optionnel, défaut: répertoire courant)"
            }
        }
    };

    public async Task<ApiResponse<ToolResult>> ExecuteAsync(JsonObject input, CancellationToken ct = default)
    {
        if (!_daemon.IsConnected)
        {
            _logger.LogWarning("[GitStatusTool] Daemon non connecté");
            return ApiResponse<ToolResult>.ErrorResponse("Daemon non connecté", 503);
        }

        var path = input["path"]?.GetValue<string>() ?? ".";

        var request = new DaemonActionRequest
        {
            RequestId = Guid.NewGuid().ToString("N"),
            Action = "git_status",
            Payload = new { path }
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
