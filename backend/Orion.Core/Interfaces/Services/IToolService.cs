using Orion.Core.DTOs;
using Orion.Core.DTOs.Responses;

namespace Orion.Core.Interfaces.Services;

/// <summary>
/// Service pour l'exécution des tools (ShiftStar, Système, etc.)
/// </summary>
public interface IToolService
{
    Task<ApiResponse<ToolResult>> ExecuteToolAsync(string toolName, string inputJson, CancellationToken ct = default);
    Task<ApiResponse<List<ToolInfoDto>>> GetAvailableToolsAsync(CancellationToken ct = default);
}
