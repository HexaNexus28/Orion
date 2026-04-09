using Orion.Core.DTOs;
using Orion.Core.DTOs.Requests;
using Orion.Core.DTOs.Responses;

namespace Orion.Core.Interfaces.Services;

/// <summary>
/// Service métier pour la gestion des conversations chat
/// Orchestration entre agents, persistence et formatage de réponse
/// </summary>
public interface IChatService
{
    Task<ApiResponse<ChatResponse>> SendMessageAsync(ChatRequest request, CancellationToken ct = default);
    Task<ApiResponse<ChatResponse>> GetConversationAsync(Guid sessionId, CancellationToken ct = default);
    Task<ApiResponse<List<ConversationSummaryDto>>> GetConversationsAsync(int page = 1, int pageSize = 20, CancellationToken ct = default);
    
    /// <summary>
    /// Stream une réponse LLM mot par mot (Server-Sent Events)
    /// Phase 4 : Pour interface temps réel
    /// </summary>
    IAsyncEnumerable<string> StreamMessageAsync(ChatRequest request, CancellationToken ct = default);
}
