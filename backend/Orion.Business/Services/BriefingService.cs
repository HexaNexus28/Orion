using Microsoft.Extensions.Logging;
using Orion.Core.DTOs.Responses;
using Orion.Core.Enums;
using Orion.Core.Interfaces.Agents;
using Orion.Core.Interfaces.Repositories;
using Orion.Core.Interfaces.Services;

namespace Orion.Business.Services;

public class BriefingService : IBriefingService
{
    private readonly IBriefingAgent _briefingAgent;
    private readonly IUnitOfWork _unitOfWork;
    private readonly ILogger<BriefingService> _logger;

    public BriefingService(IBriefingAgent briefingAgent, IUnitOfWork unitOfWork, ILogger<BriefingService> logger)
    {
        _briefingAgent = briefingAgent;
        _unitOfWork = unitOfWork;
        _logger = logger;
    }

    public async Task<ApiResponse<BriefingDto>> GenerateTodayBriefingAsync(CancellationToken ct = default)
    {
        _logger.LogInformation("[BriefingService] Requesting today's briefing from BriefingAgent");
        return await _briefingAgent.GenerateBriefingAsync(ct);
    }

    public async Task<ApiResponse<List<BriefingDto>>> GetBriefingHistoryAsync(
        int days = 7, CancellationToken ct = default)
    {
        var since = DateTime.UtcNow.AddDays(-days);
        
        // Get briefing conversations
        var conversations = await _unitOfWork.Conversations.FindAsync(
            c => c.Type == ConversationType.Briefing && c.StartedAt >= since, ct);

        var briefings = conversations.Select(c => new BriefingDto
        {
            Id = c.Id,
            Content = c.Summary ?? "Briefing sans contenu",
            CreatedAt = c.StartedAt,
            Stats = new Dictionary<string, object>()
        }).ToList();

        return ApiResponse<List<BriefingDto>>.SuccessResponse(briefings);
    }
}
