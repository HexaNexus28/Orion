using Orion.Core.DTOs.Requests;
using Orion.Core.DTOs.Responses;

namespace Orion.Core.Interfaces.Agents;

public interface IConversationAgent
{
    Task<ApiResponse<ChatResponse>> ProcessAsync(ChatRequest request, CancellationToken ct = default);

    // Streaming en 2 étapes (ApiResponse pattern) :
    // 1. PrepareStreamAsync — init session, save user msg, build prompt → ApiResponse<StreamContext>
    // 2. StreamLLMAsync — yield LLM chunks + save assistant response → IAsyncEnumerable<string>
    Task<ApiResponse<StreamContext>> PrepareStreamAsync(ChatRequest request, CancellationToken ct = default);
    IAsyncEnumerable<string> StreamLLMAsync(StreamContext context, CancellationToken ct = default);

    // Legacy — combined prepare + stream (kept for VoiceController compatibility)
    IAsyncEnumerable<string> StreamAsync(ChatRequest request, CancellationToken ct = default);
}
