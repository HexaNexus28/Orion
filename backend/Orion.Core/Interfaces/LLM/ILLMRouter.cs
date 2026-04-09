using Orion.Core.DTOs.Requests;
using Orion.Core.DTOs.Responses;

namespace Orion.Core.Interfaces.LLM;

public interface ILLMRouter
{
    Task<ApiResponse<LLMResponse>> CompleteAsync(LLMRequest request, CancellationToken ct = default);
    Task StreamAsync(LLMRequest request, Func<string, Task> onChunk, CancellationToken ct = default);
    Enums.LLMProvider ActiveProvider { get; }
}
