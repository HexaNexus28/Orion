using System.Text.Json.Nodes;
using Orion.Core.DTOs.Responses;

namespace Orion.Core.Interfaces.Tools;

public interface ITool
{
    string Name { get; }           // snake_case: "get_shiftstar_stats"
    string Description { get; }   // For the LLM
    JsonObject InputSchema { get; }
    Task<ApiResponse<ToolResult>> ExecuteAsync(JsonObject input, CancellationToken ct = default);
}
