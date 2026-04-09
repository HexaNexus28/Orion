using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.Extensions.Logging;
using Orion.Core.DTOs.Requests;
using Orion.Core.DTOs.Responses;
using Orion.Core.Interfaces.Daemon;
using Orion.Core.Interfaces.Tools;

namespace Orion.Business.Tools.System;

public class GetSystemStatusTool : ITool
{
    private readonly IDaemonClient _daemon;
    private readonly ILogger<GetSystemStatusTool> _logger;

    public GetSystemStatusTool(IDaemonClient daemon, ILogger<GetSystemStatusTool> logger)
    {
        _daemon = daemon;
        _logger = logger;
    }

    public string Name => "get_system_status";
    public string Description => "Retourne le statut du système Windows (CPU, RAM, disque, processus actifs)";

    public JsonObject InputSchema => new()
    {
        ["type"] = "object",
        ["properties"] = new JsonObject()
    };

    public async Task<ApiResponse<ToolResult>> ExecuteAsync(JsonObject input, CancellationToken ct = default)
    {
        if (!_daemon.IsConnected)
        {
            _logger.LogWarning("[GetSystemStatusTool] Daemon non connecté");
            return ApiResponse<ToolResult>.ErrorResponse("Daemon non connecté", 503);
        }

        var request = new DaemonActionRequest
        {
            RequestId = Guid.NewGuid().ToString("N"),
            Action = "system_status",
            Payload = new { }
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
