using System.Text.Json.Nodes;
using Microsoft.Extensions.Logging;
using Orion.Core.DTOs.Responses;
using Orion.Core.Interfaces.Services;
using Orion.Core.Interfaces.Tools;

namespace Orion.Business.Tools.Memory;

/// <summary>
/// profile_update - Met à jour user_profile directement
/// Priorités, préférences, etc.
/// </summary>
public class ProfileUpdateTool : ITool
{
    private readonly IMemoryService _memoryService;
    private readonly ILogger<ProfileUpdateTool> _logger;

    public string Name => "profile_update";
    
    public string Description => "Met à jour le profil utilisateur (préférences, priorités, informations personnelles).";

    public JsonObject InputSchema => new()
    {
        ["key"] = new JsonObject
        {
            ["type"] = "string",
            ["description"] = "Clé du profil à mettre à jour (ex: priority_now, language)"
        },
        ["value"] = new JsonObject
        {
            ["type"] = "string",
            ["description"] = "Nouvelle valeur"
        }
    };

    public ProfileUpdateTool(IMemoryService memoryService, ILogger<ProfileUpdateTool> logger)
    {
        _memoryService = memoryService;
        _logger = logger;
    }

    public async Task<ApiResponse<ToolResult>> ExecuteAsync(JsonObject input, CancellationToken ct = default)
    {
        try
        {
            var key = input["key"]?.ToString();
            var value = input["value"]?.ToString();

            if (string.IsNullOrWhiteSpace(key) || string.IsNullOrWhiteSpace(value))
            {
                return ApiResponse<ToolResult>.ErrorResponse(
                    "Both 'key' and 'value' are required for profile_update",
                    400);
            }

            _logger.LogInformation("Updating profile: {Key} = {Value}", key, value);

            var result = await _memoryService.UpdateUserProfileAsync(key, value, ct);

            if (result.Success)
            {
                return ApiResponse<ToolResult>.SuccessResponse(
                    ToolResult.SuccessResult(null, Name),
                    $"Profil mis à jour: {key}");
            }

            return ApiResponse<ToolResult>.ErrorResponse(
                result.Message ?? "Failed to update profile",
                result.StatusCode);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error in profile_update");
            return ApiResponse<ToolResult>.ErrorResponse(
                ToolResult.FromException(ex, Name).Error ?? "Unknown error",
                500);
        }
    }
}
