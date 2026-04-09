using Orion.Core.DTOs.Responses;

namespace Orion.Core.Interfaces.Agents;

// Scaffold - to be fully implemented with IHostedService for cron
public interface IBriefingAgent
{
    Task<ApiResponse<BriefingDto>> GenerateBriefingAsync(CancellationToken ct = default);
}
