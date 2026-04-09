using Orion.Core.DTOs.Requests;
using Orion.Core.DTOs.Responses;

namespace Orion.Core.Interfaces.LLM;

// IMMUTABLE - Do not modify this interface
public interface ILLMClient
{
    Task<ApiResponse<LLMResponse>> CompleteAsync(LLMRequest request, CancellationToken ct = default);
    Task StreamAsync(LLMRequest request, Func<string, Task> onChunk, CancellationToken ct = default);
    bool IsAvailable();
    Enums.LLMProvider Provider { get; }
}
