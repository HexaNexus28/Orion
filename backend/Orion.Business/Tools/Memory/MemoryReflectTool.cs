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
    private readonly IMemoryConsolidator _consolidator;
    private readonly ILogger<MemoryReflectTool> _logger;

    public string Name => "memory_reflect";
    
    public string Description => "Consolide la mémoire : relit les échanges bruts et en distille les faits durables (règles, décisions, références, état en cours). À lancer en fin de session ou quand la mémoire s'alourdit.";

    public JsonObject InputSchema => new()
    {
        ["type"] = "object",
        ["properties"] = new JsonObject
        {
            ["format"] = new JsonObject
            {
                ["type"] = "string",
                ["description"] = "Format de la synthèse : short (défaut) ou detailed"
            }
        }
    };

    public MemoryReflectTool(IMemoryConsolidator consolidator, ILogger<MemoryReflectTool> logger)
    {
        _consolidator = consolidator;
        _logger = logger;
    }

    public async Task<ApiResponse<ToolResult>> ExecuteAsync(JsonObject input, CancellationToken ct = default)
    {
        try
        {
            _logger.LogInformation("[memory_reflect] Consolidation demandee");

            var result = await _consolidator.ConsolidateAsync(ct);

            if (result.Success && result.Data != null)
            {
                var rapport = result.Data;
                return ApiResponse<ToolResult>.SuccessResponse(
                    ToolResult.SuccessResult(new
                    {
                        resume = rapport.Resume,
                        episodesExamines = rapport.EpisodesExamines,
                        souvenirsEcrits = rapport.SouvenirsEcrits,
                        etatsPerimesSupprimes = rapport.EtatsPerimesSupprimes,
                        distilles = rapport.Distilles
                    }, Name),
                    rapport.Resume);
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
