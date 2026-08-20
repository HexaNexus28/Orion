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
    /// Déroule la boucle agent et émet des événements typés (tokens, appels d'outils, fin).
    /// L'UI peut ainsi montrer ce qu'ORION FAIT, pas seulement ce qu'il dit.
    /// </summary>
    IAsyncEnumerable<AgentEvent> StreamMessageAsync(ChatRequest request, CancellationToken ct = default);
}
