using System.Text.Json.Nodes;
using Microsoft.Extensions.Logging;
using Orion.Core.DTOs.Responses;
using Orion.Core.Interfaces.Services;
using Orion.Core.Interfaces.Tools;

namespace Orion.Business.Tools.Memory;

/// <summary>
/// memory_reflect - Synthèse hebdomadaire autonome
/// Relit les souvenirs, génère des patterns
/// Appelé par BriefingAgent chaque dimanche 23h
/// </summary>
public class MemoryReflectTool : ITool
{
    private readonly IMemoryService _memoryService;
    private readonly ILogger<MemoryReflectTool> _logger;

    public string Name => "memory_reflect";
    
    public string Description => "Génère une synthèse hebdomadaire des souvenirs. Analyse les patterns et produit un résumé des apprentissages de la semaine.";

    public JsonObject InputSchema => new()
    {
        ["format"] = new JsonObject
        {
            ["type"] = "string",
            ["description"] = "Format du résumé (short, detailed, default: short)"
        }
    };

    public MemoryReflectTool(IMemoryService memoryService, ILogger<MemoryReflectTool> logger)
    {
        _memoryService = memoryService;
        _logger = logger;
    }

    public async Task<ApiResponse<ToolResult>> ExecuteAsync(JsonObject input, CancellationToken ct = default)
    {
        try
        {
            var format = input["format"]?.ToString() ?? "short";
            
            _logger.LogInformation("Memory reflection started (format: {Format})", format);

            var result = await _memoryService.ReflectAsync(ct);

            if (result.Success && result.Data != null)
            {
                return ApiResponse<ToolResult>.SuccessResponse(
                    ToolResult.SuccessResult(new { reflection = result.Data }, Name),
                    "Réflexion terminée");
            }

            return ApiResponse<ToolResult>.ErrorResponse(
                result.Message ?? "Failed to generate reflection",
                result.StatusCode);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error in memory_reflect");
            return ApiResponse<ToolResult>.ErrorResponse(
                ToolResult.FromException(ex, Name).Error ?? "Unknown error",
                500);
        }
    }
}
