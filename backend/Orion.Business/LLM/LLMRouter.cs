using Microsoft.Extensions.Logging;
using Orion.Core.DTOs.Requests;
using Orion.Core.DTOs.Responses;
using Orion.Core.Enums;
using Orion.Core.Interfaces.LLM;

namespace Orion.Business.LLM;

public class LLMRouter : ILLMRouter
{
    private readonly ILLMClient _ollamaClient;
    private readonly ILogger<LLMRouter> _logger;

    public LLMRouter(IEnumerable<ILLMClient> clients, ILogger<LLMRouter> logger)
    {
        _logger = logger;
        _ollamaClient = clients.FirstOrDefault(c => c.Provider == LLMProvider.Ollama)
            ?? throw new InvalidOperationException("Ollama client not registered");
    }

    public LLMProvider ActiveProvider => _ollamaClient.IsAvailable() ? LLMProvider.Ollama : LLMProvider.None;

    public async Task<ApiResponse<LLMResponse>> CompleteAsync(LLMRequest request, CancellationToken ct = default)
    {
        if (!_ollamaClient.IsAvailable())
        {
            _logger.LogError("[LLMRouter] Ollama unavailable — assure-toi qu'Ollama est lancé");
            return ApiResponse<LLMResponse>.ErrorResponse("Ollama non disponible. Lance Ollama et réessaie.", 503);
        }

        _logger.LogInformation("[LLMRouter] Routing to Ollama");
        return await _ollamaClient.CompleteAsync(request, ct);
    }

    public async Task StreamAsync(LLMRequest request, Func<string, Task> onChunk, CancellationToken ct = default)
    {
        if (!_ollamaClient.IsAvailable())
        {
            _logger.LogError("[LLMRouter] Ollama unavailable for streaming");
            throw new InvalidOperationException("Ollama non disponible. Lance Ollama et réessaie.");
        }

        _logger.LogInformation("[LLMRouter] Streaming from Ollama");
        await _ollamaClient.StreamAsync(request, onChunk, ct);
    }
}
