using Orion.Core.DTOs.Requests;
using Orion.Core.DTOs.Responses;
using Orion.Core.Enums;

namespace Orion.Core.Interfaces.Services;

/// <summary>
/// Service pour l'inférence LLM (Ollama/Anthropic)
/// Abstraction du LLMRouter pour la couche API
/// </summary>
public interface ILLMService
{
    Task<ApiResponse<LLMResponse>> CompleteAsync(LLMRequest request, CancellationToken ct = default);
    Task<ApiResponse<LLMResponse>> CompleteWithPromptAsync(string systemPrompt, string userMessage, CancellationToken ct = default);
    Task StreamAsync(LLMRequest request, Func<string, Task> onChunk, CancellationToken ct = default);
    Task<ApiResponse<bool>> IsAvailableAsync(CancellationToken ct = default);
    LLMProvider GetActiveProvider();
}
