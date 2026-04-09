using System.Text.Json.Nodes;
using Microsoft.Extensions.Logging;
using Orion.Core.DTOs.Responses;
using Orion.Core.Interfaces.Services;
using Orion.Core.Interfaces.Tools;

namespace Orion.Business.Tools.Memory;

/// <summary>
/// memory_forget - Supprime un souvenir obsolète ou incorrect
/// </summary>
public class MemoryForgetTool : ITool
{
    private readonly IMemoryService _memoryService;
    private readonly ILogger<MemoryForgetTool> _logger;

    public string Name => "memory_forget";
    
    public string Description => "Supprime un souvenir obsolète ou incorrect de la mémoire.";

    public JsonObject InputSchema => new()
    {
        ["id"] = new JsonObject
        {
            ["type"] = "string",
            ["description"] = "ID du souvenir à supprimer"
        }
    };

    public MemoryForgetTool(IMemoryService memoryService, ILogger<MemoryForgetTool> logger)
    {
        _memoryService = memoryService;
        _logger = logger;
    }

    public async Task<ApiResponse<ToolResult>> ExecuteAsync(JsonObject input, CancellationToken ct = default)
    {
        try
        {
            var id = input["id"]?.ToString();

            if (string.IsNullOrWhiteSpace(id))
            {
                return ApiResponse<ToolResult>.ErrorResponse(
                    "'id' is required for memory_forget",
                    400);
            }

            _logger.LogInformation("Forgetting memory {MemoryId}", id);

            var result = await _memoryService.DeleteMemoryAsync(id, ct);

            if (result.Success)
            {
                return ApiResponse<ToolResult>.SuccessResponse(
                    ToolResult.SuccessResult(null, Name),
                    "Souvenir supprimé avec succès");
            }

            return ApiResponse<ToolResult>.ErrorResponse(
                result.Message ?? "Failed to delete memory",
                result.StatusCode);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error in memory_forget");
            return ApiResponse<ToolResult>.ErrorResponse(
                ToolResult.FromException(ex, Name).Error ?? "Unknown error",
                500);
        }
    }
}
