using System.Text.Json.Nodes;
using Microsoft.Extensions.Logging;
using Orion.Core.DTOs.Responses;
using Orion.Core.Interfaces.Services;
using Orion.Core.Interfaces.Tools;

namespace Orion.Business.Tools.Memory;

/// <summary>
/// memory_save - Sauvegarde un fait important
/// ORION décide seul quand c'est critique
/// Ex: utilisateur annonce qu'Areas France a signé → ORION sauvegarde sans qu'on demande
/// </summary>
public class MemorySaveTool : ITool
{
    private readonly IMemoryService _memoryService;
    private readonly ILogger<MemorySaveTool> _logger;

    public string Name => "memory_save";
    
    public string Description => "Sauvegarde un souvenir important dans la mémoire long-terme. ORION utilise ce tool automatiquement pour les faits critiques (projets, priorités, préférences).";

    public JsonObject InputSchema => new()
    {
        ["content"] = new JsonObject
        {
            ["type"] = "string",
            ["description"] = "Le contenu du souvenir à sauvegarder"
        },
        ["source"] = new JsonObject
        {
            ["type"] = "string",
            ["description"] = "Source du souvenir (conversation, email, etc.)"
        },
        ["importance"] = new JsonObject
        {
            ["type"] = "number",
            ["description"] = "Importance du souvenir (0.5-2.0, défaut: 1.0)"
        }
    };

    public MemorySaveTool(IMemoryService memoryService, ILogger<MemorySaveTool> logger)
    {
        _memoryService = memoryService;
        _logger = logger;
    }

    public async Task<ApiResponse<ToolResult>> ExecuteAsync(JsonObject input, CancellationToken ct = default)
    {
        try
        {
            var content = input["content"]?.ToString();
            var source = input["source"]?.ToString() ?? "conversation";
            var importance = input["importance"]?.GetValue<float>() ?? 1.0f;

            if (string.IsNullOrWhiteSpace(content))
            {
                return ApiResponse<ToolResult>.ErrorResponse(
                    "Content is required for memory_save",
                    400);
            }

            _logger.LogInformation("Saving memory: {ContentPreview}...", 
                content.Length > 50 ? content[..50] : content);

            var result = await _memoryService.SaveMemoryAsync(content, source, importance, ct);

            if (result.Success)
            {
                return ApiResponse<ToolResult>.SuccessResponse(
                    ToolResult.SuccessResult(null, Name),
                    "Souvenir sauvegardé avec succès");
            }

            return ApiResponse<ToolResult>.ErrorResponse(
                result.Message ?? "Failed to save memory",
                result.StatusCode);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error in memory_save");
            return ApiResponse<ToolResult>.ErrorResponse(
                ToolResult.FromException(ex, Name).Error ?? "Unknown error",
                500);
        }
    }
}
