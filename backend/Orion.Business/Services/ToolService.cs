using Microsoft.Extensions.Logging;
using Orion.Core.DTOs;
using Orion.Core.DTOs.Responses;
using Orion.Core.Interfaces.Services;
using Orion.Core.DTOs.Internal.Tools;
using Orion.Core.Interfaces.Tools;
using System.Text.Json;

namespace Orion.Business.Services;

public class ToolService : IToolService
{
    private readonly IToolRegistry _toolRegistry;
    private readonly IToolInvoker _toolInvoker;
    private readonly ILogger<ToolService> _logger;

    public ToolService(IToolRegistry toolRegistry, IToolInvoker toolInvoker, ILogger<ToolService> logger)
    {
        _toolRegistry = toolRegistry;
        _toolInvoker = toolInvoker;
        _logger = logger;
    }

    /// <summary>
    /// Exécute un outil demandé par l'API. Passe par <see cref="IToolInvoker"/>, comme la
    /// conversation : un appel manuel avec le PC éteint se met en file exactement de la même
    /// façon, au lieu de rendre un 503 que ce chemin-ci aurait interprété à sa manière.
    ///
    /// L'origine reste `chat` : cet appel vient toujours d'une demande humaine.
    /// </summary>
    public async Task<ApiResponse<ToolResult>> ExecuteToolAsync(
        string toolName, string inputJson, CancellationToken ct = default)
    {
        try
        {
            var input = System.Text.Json.Nodes.JsonNode.Parse(inputJson)?.AsObject()
                ?? new System.Text.Json.Nodes.JsonObject();

            return await _toolInvoker.InvokeAsync(toolName, input, ToolInvocationContext.Direct, ct);
        }
        catch (System.Text.Json.JsonException ex)
        {
            _logger.LogWarning(ex, "Arguments illisibles pour {ToolName}", toolName);
            return ApiResponse<ToolResult>.ErrorResponse($"Arguments JSON invalides : {ex.Message}", 400);
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
