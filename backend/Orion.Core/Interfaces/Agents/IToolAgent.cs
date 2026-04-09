using System.Text.Json.Nodes;
using Orion.Core.DTOs.Responses;

namespace Orion.Core.Interfaces.Agents;

// Scaffold - to be fully implemented
public interface IToolAgent
{
    Task<ApiResponse<ToolResult>> ExecuteToolAsync(string toolName, JsonObject input, CancellationToken ct = default);
}
