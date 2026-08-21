using System.Text.Json;
using System.Text.Json.Nodes;
using Orion.Core.DTOs.Requests;
using Orion.Core.DTOs.Responses;
using Orion.Core.Interfaces.Daemon;
using Orion.Core.Interfaces.Tools;

namespace Orion.Business.Tools.System;

public class KillProcessTool : ITool
{
    private readonly IDaemonClient _daemon;

    public KillProcessTool(IDaemonClient daemon)
    {
        _daemon = daemon;
    }

    public string Name => "kill_process";
    public string Description => "Termine un processus Windows par nom ou PID";

    public bool RequiresDaemon => true;
    public bool IsDestructive => true;

    public JsonObject InputSchema => new()
    {
        ["type"] = "object",
        ["properties"] = new JsonObject
        {
            ["name"] = new JsonObject { ["type"] = "string", ["description"] = "Nom du processus à tuer (ex: chrome, notepad)" },
            ["pid"] = new JsonObject { ["type"] = "integer", ["description"] = "PID du processus (alternatif au nom)" }
        }
    };

    public async Task<ApiResponse<ToolResult>> ExecuteAsync(JsonObject input, CancellationToken ct = default)
    {

        var name = input["name"]?.GetValue<string>();
        var pid = input["pid"]?.GetValue<int>();

        if (string.IsNullOrWhiteSpace(name) && pid == null)
            return ApiResponse<ToolResult>.ErrorResponse("Paramètre name ou pid requis", 400);

        var result = await _daemon.SendActionAsync(new DaemonActionRequest
        {
            RequestId = Guid.NewGuid().ToString("N"),
            Action = "kill_process",
            Payload = new { name, pid }
        }, ct);

        if (!result.Success)
            return ApiResponse<ToolResult>.ErrorResponse(result.Message ?? "Daemon error", result.StatusCode);

        return ApiResponse<ToolResult>.SuccessResponse(ToolResult.SuccessResult(
            JsonSerializer.Serialize(result.Data?.Data), Name));
    }
}
