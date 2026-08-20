using Orion.Core.DTOs;
using Orion.Core.DTOs.Responses;
using Orion.Core.Enums;
using Orion.Core.Interfaces.Services;

namespace Orion.Business.Services;

public class HealthService : IHealthService
{
    private readonly ILLMService _llmService;

    public HealthService(ILLMService llmService)
    {
        _llmService = llmService;
    }

    public ApiResponse<HealthCheckDto> GetHealthStatus()
    {
        var status = _llmService.GetStatus();
        var llm = status.Data;

        var health = new HealthCheckDto
        {
            Status = "healthy",
            LlmProvider = llm is null || llm.Provider == LLMProvider.None
                ? nameof(LLMProvider.None)
                : llm.Provider.ToString(),
            LlmModel = llm?.Model ?? "aucun",
            Timestamp = DateTime.UtcNow
        };

        return ApiResponse<HealthCheckDto>.SuccessResponse(health);
    }
}
