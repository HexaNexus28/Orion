using System.Text.Json;
using System.Text.Json.Nodes;
using Orion.Core.DTOs.Requests;
using Orion.Core.DTOs.Responses;
using Orion.Core.Interfaces.Daemon;
using Orion.Core.Interfaces.Tools;

namespace Orion.Business.Tools.System;

public class GitCommitTool : ITool
{
    private readonly IDaemonClient _daemon;

    public GitCommitTool(IDaemonClient daemon)
    {
        _daemon = daemon;
    }

    public string Name => "git_commit";
    public string Description => "Effectue un commit git rapide avec un message depuis ORION";

    public bool RequiresDaemon => true;
    public bool IsDestructive => true;

    /// <summary>« Commit le travail » depuis le téléphone à 22 h : c'est LE cas d'usage de la file.</summary>
    public bool IsDeferrable => true;

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
