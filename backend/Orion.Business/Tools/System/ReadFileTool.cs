using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.Extensions.Logging;
using Orion.Core.DTOs.Requests;
using Orion.Core.DTOs.Responses;
using Orion.Core.Interfaces.Daemon;
using Orion.Core.Interfaces.Tools;

namespace Orion.Business.Tools.System;

public class ReadFileTool : ITool
{
    private readonly IDaemonClient _daemon;
    private readonly ILogger<ReadFileTool> _logger;

    public ReadFileTool(IDaemonClient daemon, ILogger<ReadFileTool> logger)
    {
        _daemon = daemon;
        _logger = logger;
    }

    public string Name => "read_file";
    public string Description => "Lit le contenu d'un fichier local sur le PC Windows";

    public JsonObject InputSchema => new()
    {
        ["type"] = "object",
        ["properties"] = new JsonObject
        {
            ["filePath"] = new JsonObject
            {
                ["type"] = "string",
                ["description"] = "Chemin absolu ou relatif du fichier à lire"
            }
        },
        ["required"] = new JsonArray { "filePath" }
    };

    public async Task<ApiResponse<ToolResult>> ExecuteAsync(JsonObject input, CancellationToken ct = default)
    {
        if (!_daemon.IsConnected)
        {
            _logger.LogWarning("[ReadFileTool] Daemon non connecté");
            return ApiResponse<ToolResult>.ErrorResponse("Daemon non connecté", 503);
        }

        var filePath = input["filePath"]?.GetValue<string>();
        if (string.IsNullOrWhiteSpace(filePath))
        {
            return ApiResponse<ToolResult>.ErrorResponse("Paramètre filePath requis", 400);
        }

        var request = new DaemonActionRequest
        {
            RequestId = Guid.NewGuid().ToString("N"),
            Action = "read_file",
            Payload = new { filePath }
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
