using Microsoft.Extensions.Logging;
using Orion.Core.DTOs.Responses;
using Orion.Core.Entities;
using Orion.Core.Enums;
using Orion.Core.Interfaces.Repositories;
using Orion.Core.Interfaces.Services;

namespace Orion.Business.Services;

public class BriefingService : IBriefingService
{
    private readonly IUnitOfWork _unitOfWork;
    private readonly ILogger<BriefingService> _logger;

    public BriefingService(IUnitOfWork unitOfWork, ILogger<BriefingService> logger)
    {
        _unitOfWork = unitOfWork;
        _logger = logger;
    }

    public async Task<ApiResponse<BriefingDto>> GenerateTodayBriefingAsync(CancellationToken ct = default)
    {
        await Task.Yield(); // Suppress async warning until fully implemented
        
        // TODO: Implement briefing generation logic
        // 1. Get ShiftStar stats
        // 2. Get calendar events
        // 3. Get emails
        // 4. Generate summary with LLM
        
        _logger.LogInformation("Generating today's briefing");
        
        var briefing = new BriefingDto
        {
            Id = Guid.NewGuid(),
            Content = "Briefing du jour - À implémenter",
            CreatedAt = DateTime.UtcNow,
            Stats = new Dictionary<string, object>()
        };

        return ApiResponse<BriefingDto>.SuccessResponse(briefing);
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
