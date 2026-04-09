using Orion.Core.DTOs.Requests;
using Orion.Core.DTOs.Responses;

namespace Orion.Core.Interfaces.Agents;

public interface IConversationAgent
{
    Task<ApiResponse<ChatResponse>> ProcessAsync(ChatRequest request, CancellationToken ct = default);
}
