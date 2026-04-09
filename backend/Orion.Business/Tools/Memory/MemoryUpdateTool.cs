using System.Text.Json.Nodes;
using Microsoft.Extensions.Logging;
using Orion.Core.DTOs.Responses;
using Orion.Core.Interfaces.Services;
using Orion.Core.Interfaces.Tools;

namespace Orion.Business.Tools.Memory;

/// <summary>
/// memory_update - Met à jour un souvenir existant
/// Évite les doublons en mettant à jour plutôt que créer
/// </summary>
public class MemoryUpdateTool : ITool
{
    private readonly IMemoryService _memoryService;
    private readonly ILogger<MemoryUpdateTool> _logger;

    public string Name => "memory_update";
    
    public string Description => "Met à jour un souvenir existant. Utilisé pour corriger ou enrichir un souvenir déjà sauvegardé.";

    public JsonObject InputSchema => new()
    {
        ["id"] = new JsonObject
        {
            ["type"] = "string",
            ["description"] = "ID du souvenir à mettre à jour"
        },
        ["content"] = new JsonObject
        {
            ["type"] = "string",
            ["description"] = "Nouveau contenu du souvenir"
        }
    };

    public MemoryUpdateTool(IMemoryService memoryService, ILogger<MemoryUpdateTool> logger)
    {
        _memoryService = memoryService;
        _logger = logger;
    }

    public async Task<ApiResponse<ToolResult>> ExecuteAsync(JsonObject input, CancellationToken ct = default)
    {
        try
        {
            var id = input["id"]?.ToString();
            var content = input["content"]?.ToString();

            if (string.IsNullOrWhiteSpace(id) || string.IsNullOrWhiteSpace(content))
            {
                return ApiResponse<ToolResult>.ErrorResponse(
                    "Both 'id' and 'content' are required for memory_update",
                    400);
            }

            _logger.LogInformation("Updating memory {MemoryId}", id);

            var result = await _memoryService.UpdateMemoryAsync(id, content, ct);

            if (result.Success)
            {
                return ApiResponse<ToolResult>.SuccessResponse(
                    ToolResult.SuccessResult(null, Name),
                    "Souvenir mis à jour avec succès");
            }

            return ApiResponse<ToolResult>.ErrorResponse(
                result.Message ?? "Failed to update memory",
                result.StatusCode);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error in memory_update");
            return ApiResponse<ToolResult>.ErrorResponse(
                ToolResult.FromException(ex, Name).Error ?? "Unknown error",
                500);
        }
    }
}
