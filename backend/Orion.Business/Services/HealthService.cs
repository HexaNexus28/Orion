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
        var provider = _llmService.GetActiveProvider();
        var providerName = provider == LLMProvider.None || provider == default 
            ? "None" 
            : provider.ToString();
        
        var health = new HealthCheckDto
        {
            Status = "healthy",
            LlmProvider = providerName,
            Timestamp = DateTime.UtcNow
        };

        return ApiResponse<HealthCheckDto>.SuccessResponse(health);
    }
}
