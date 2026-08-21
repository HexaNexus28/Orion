using System.Text.Json;
using System.Text.Json.Nodes;
using Orion.Core.DTOs.Requests;
using Orion.Core.DTOs.Responses;
using Orion.Core.Interfaces.Daemon;
using Orion.Core.Interfaces.Tools;

namespace Orion.Business.Tools.System;

public class ListFilesTool : ITool
{
    private readonly IDaemonClient _daemon;

    public ListFilesTool(IDaemonClient daemon)
    {
        _daemon = daemon;
    }

    public string Name => "list_files";
    public string Description => "Liste les fichiers et dossiers d'un répertoire sur le PC Windows";

    public bool RequiresDaemon => true;

    public JsonObject InputSchema => new()
    {
        ["type"] = "object",
        ["properties"] = new JsonObject
        {
            ["path"] = new JsonObject { ["type"] = "string", ["description"] = "Chemin du répertoire à lister" },
            ["pattern"] = new JsonObject { ["type"] = "string", ["description"] = "Filtre glob optionnel, ex: *.cs" },
            ["recursive"] = new JsonObject { ["type"] = "boolean", ["description"] = "Lister récursivement (défaut: false)" }
        },
        ["required"] = new JsonArray { "path" }
    };

    public async Task<ApiResponse<ToolResult>> ExecuteAsync(JsonObject input, CancellationToken ct = default)
    {

        var path = input["path"]?.GetValue<string>();
        if (string.IsNullOrWhiteSpace(path))
            return ApiResponse<ToolResult>.ErrorResponse("Paramètre path requis", 400);

        var pattern = input["pattern"]?.GetValue<string>() ?? "*";
        var recursive = input["recursive"]?.GetValue<bool>() ?? false;

        var result = await _daemon.SendActionAsync(new DaemonActionRequest
        {
            RequestId = Guid.NewGuid().ToString("N"),
            Action = "list_files",
            Payload = new { path, pattern, recursive }
        }, ct);

        if (!result.Success)
            return ApiResponse<ToolResult>.ErrorResponse(result.Message ?? "Daemon error", result.StatusCode);

        return ApiResponse<ToolResult>.SuccessResponse(ToolResult.SuccessResult(
            JsonSerializer.Serialize(result.Data?.Data), Name));
    }
}
