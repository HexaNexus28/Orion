using Microsoft.Extensions.Logging;
using Orion.Core.DTOs;
using Orion.Core.DTOs.Responses;
using Orion.Core.Interfaces.Services;
using Orion.Core.Interfaces.Tools;
using System.Text.Json;

namespace Orion.Business.Services;

public class ToolService : IToolService
{
    private readonly IToolRegistry _toolRegistry;
    private readonly ILogger<ToolService> _logger;

    public ToolService(IToolRegistry toolRegistry, ILogger<ToolService> logger)
    {
        _toolRegistry = toolRegistry;
        _logger = logger;
    }

    public async Task<ApiResponse<ToolResult>> ExecuteToolAsync(
        string toolName, string inputJson, CancellationToken ct = default)
    {
        var tool = _toolRegistry.GetTool(toolName);
        
        if (tool == null)
        {
            return ApiResponse<ToolResult>.NotFoundResponse($"Tool '{toolName}' not found");
        }

        try
        {
            var input = System.Text.Json.Nodes.JsonNode.Parse(inputJson)?.AsObject()
                ?? new System.Text.Json.Nodes.JsonObject();
            
            var result = await tool.ExecuteAsync(input, ct);
            return result;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Tool execution failed: {ToolName}", toolName);
            return ApiResponse<ToolResult>.ErrorResponse($"Tool execution failed: {ex.Message}", 500);
        }
    }

    public Task<ApiResponse<List<ToolInfoDto>>> GetAvailableToolsAsync(CancellationToken ct = default)
    {
        var tools = _toolRegistry.GetAllTools().Select(t => new ToolInfoDto
        {
            Name = t.Name,
            Description = t.Description,
            InputSchema = t.InputSchema.ToJsonString()
        }).ToList();

        return Task.FromResult(ApiResponse<List<ToolInfoDto>>.SuccessResponse(tools));
    }
}
