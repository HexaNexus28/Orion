using Orion.Core.DTOs.Responses;

namespace Orion.Core.Interfaces.Services;

/// <summary>
/// Service pour le morning briefing proactif
/// </summary>
public interface IBriefingService
{
    Task<ApiResponse<BriefingDto>> GenerateTodayBriefingAsync(CancellationToken ct = default);
    Task<ApiResponse<List<BriefingDto>>> GetBriefingHistoryAsync(int days = 7, CancellationToken ct = default);
}
