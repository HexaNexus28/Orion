using Orion.Core.DTOs.Responses;

namespace Orion.Core.Interfaces.Agents;

public interface IBriefingAgent
{
    Task<ApiResponse<BriefingDto>> GenerateBriefingAsync(CancellationToken ct = default);
    Task<ApiResponse<string>> GenerateProactiveMessageAsync(string pattern, string context, CancellationToken ct = default);
}
