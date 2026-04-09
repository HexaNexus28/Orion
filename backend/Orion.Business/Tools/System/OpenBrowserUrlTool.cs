using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.Extensions.Logging;
using Orion.Core.DTOs.Requests;
using Orion.Core.DTOs.Responses;
using Orion.Core.Interfaces.Daemon;
using Orion.Core.Interfaces.Tools;

namespace Orion.Business.Tools.System;

public class OpenBrowserUrlTool : ITool
{
    private readonly IDaemonClient _daemon;
    private readonly ILogger<OpenBrowserUrlTool> _logger;

    public OpenBrowserUrlTool(IDaemonClient daemon, ILogger<OpenBrowserUrlTool> logger)
    {
        _daemon = daemon;
        _logger = logger;
    }

    public string Name => "open_browser_url";
    public string Description => "Ouvre une URL dans le navigateur par défaut du PC";

    public JsonObject InputSchema => new()
    {
        ["type"] = "object",
        ["properties"] = new JsonObject
        {
            ["url"] = new JsonObject
            {
                ["type"] = "string",
                ["description"] = "URL à ouvrir dans le navigateur (doit commencer par http:// ou https://)"
            }
        },
        ["required"] = new JsonArray { "url" }
    };

    public async Task<ApiResponse<ToolResult>> ExecuteAsync(JsonObject input, CancellationToken ct = default)
    {
        if (!_daemon.IsConnected)
        {
            _logger.LogWarning("[OpenBrowserUrlTool] Daemon non connecté");
            return ApiResponse<ToolResult>.ErrorResponse("Daemon non connecté", 503);
        }

        var url = input["url"]?.GetValue<string>();
        if (string.IsNullOrWhiteSpace(url))
        {
            return ApiResponse<ToolResult>.ErrorResponse("Paramètre url requis", 400);
        }

        var request = new DaemonActionRequest
        {
            RequestId = Guid.NewGuid().ToString("N"),
            Action = "open_browser_url",
            Payload = new { url }
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
