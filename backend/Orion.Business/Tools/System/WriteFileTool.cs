using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.Extensions.Logging;
using Orion.Core.DTOs.Requests;
using Orion.Core.DTOs.Responses;
using Orion.Core.Interfaces.Daemon;
using Orion.Core.Interfaces.Tools;

namespace Orion.Business.Tools.System;

public class WriteFileTool : ITool
{
    private readonly IDaemonClient _daemon;
    private readonly ILogger<WriteFileTool> _logger;

    public WriteFileTool(IDaemonClient daemon, ILogger<WriteFileTool> logger)
    {
        _daemon = daemon;
        _logger = logger;
    }

    public string Name => "write_file";
    public string Description => "Écrit ou écrase le contenu d'un fichier local sur le PC Windows";

    public bool RequiresDaemon => true;
    public bool IsDestructive => true;

    public JsonObject InputSchema => new()
    {
        ["type"] = "object",
        ["properties"] = new JsonObject
        {
            ["path"] = new JsonObject { ["type"] = "string", ["description"] = "Chemin absolu du fichier à écrire" },
            ["content"] = new JsonObject { ["type"] = "string", ["description"] = "Contenu à écrire dans le fichier" }
        },
        ["required"] = new JsonArray { "path", "content" }
    };

    public async Task<ApiResponse<ToolResult>> ExecuteAsync(JsonObject input, CancellationToken ct = default)
    {
        if (!_daemon.IsConnected)
            return ApiResponse<ToolResult>.ErrorResponse("Daemon non connecté", 503);

        var path = input["path"]?.GetValue<string>();
        var content = input["content"]?.GetValue<string>();

        if (string.IsNullOrWhiteSpace(path))
            return ApiResponse<ToolResult>.ErrorResponse("Paramètre path requis", 400);

        if (content is null)
            return ApiResponse<ToolResult>.ErrorResponse("Paramètre content requis", 400);

        var result = await _daemon.SendActionAsync(new DaemonActionRequest
        {
            RequestId = Guid.NewGuid().ToString("N"),
            Action = "write_file",
            Payload = new { path, content }
        }, ct);

        if (!result.Success)
            return ApiResponse<ToolResult>.ErrorResponse(result.Message ?? "Daemon error", result.StatusCode);

        return ApiResponse<ToolResult>.SuccessResponse(ToolResult.SuccessResult(
            JsonSerializer.Serialize(result.Data?.Data), Name));
    }
}
