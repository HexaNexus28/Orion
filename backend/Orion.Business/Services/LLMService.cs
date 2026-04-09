using Microsoft.Extensions.Logging;
using Orion.Core.DTOs.Requests;
using Orion.Core.DTOs.Responses;
using Orion.Core.Enums;
using Orion.Core.Interfaces.LLM;
using Orion.Core.Interfaces.Services;

namespace Orion.Business.Services;

public class LLMService : ILLMService
{
    private readonly ILLMRouter _llmRouter;
    private readonly ILogger<LLMService> _logger;

    public LLMService(ILLMRouter llmRouter, ILogger<LLMService> logger)
    {
        _llmRouter = llmRouter;
        _logger = logger;
    }

    public async Task<ApiResponse<LLMResponse>> CompleteAsync(LLMRequest request, CancellationToken ct = default)
    {
        return await _llmRouter.CompleteAsync(request, ct);
    }

    public async Task<ApiResponse<LLMResponse>> CompleteWithPromptAsync(
        string systemPrompt, string userMessage, CancellationToken ct = default)
    {
        var request = new LLMRequest
        {
            SystemPrompt = systemPrompt,
            Messages = new List<LLMMessage>
            {
                new() { Role = "user", Content = userMessage }
            }
        };

        return await _llmRouter.CompleteAsync(request, ct);
    }

    public async Task StreamAsync(LLMRequest request, Func<string, Task> onChunk, CancellationToken ct = default)
    {
        await _llmRouter.StreamAsync(request, onChunk, ct);
    }

    public Task<ApiResponse<bool>> IsAvailableAsync(CancellationToken ct = default)
    {
        var isAvailable = _llmRouter.ActiveProvider != LLMProvider.None;
        return Task.FromResult(ApiResponse<bool>.SuccessResponse(isAvailable));
    }

    public LLMProvider GetActiveProvider()
    {
        return _llmRouter.ActiveProvider;
    }
}
