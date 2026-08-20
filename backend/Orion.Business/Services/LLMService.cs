using Orion.Core.DTOs.Responses;
using Orion.Core.Enums;
using Orion.Core.Interfaces.LLM;
using Orion.Core.Interfaces.Services;

namespace Orion.Business.Services;

public class LLMService : ILLMService
{
    private readonly ILLMAgentClient _llmClient;

    public LLMService(ILLMAgentClient llmClient)
    {
        _llmClient = llmClient;
    }

    public ApiResponse<LLMStatusDto> GetStatus()
    {
        var provider = _llmClient.Provider;

        return ApiResponse<LLMStatusDto>.SuccessResponse(new LLMStatusDto
        {
            Provider = provider,
            Model = _llmClient.ModelId,
            IsOnline = provider != LLMProvider.None
        });
    }
}
