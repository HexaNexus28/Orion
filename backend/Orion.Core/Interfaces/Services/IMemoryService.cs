using Orion.Core.DTOs;
using Orion.Core.DTOs.Responses;

namespace Orion.Core.Interfaces.Services;

/// <summary>
/// Service pour la mémoire long-terme (RAG) et embeddings
/// + Tools ORION autonomes (save, update, forget, reflect, profile)
/// </summary>
public interface IMemoryService
{
    // === Recherche et RAG ===
    Task<ApiResponse<List<MemoryVectorDto>>> SearchSimilarAsync(string query, int topK = 5, CancellationToken ct = default);
    
    // === Gestion mémoire de base ===
    Task<ApiResponse<bool>> SaveMemoryAsync(string content, string source, float importance = 1.0f, CancellationToken ct = default);
    Task<ApiResponse<bool>> UpdateMemoryAsync(string id, string content, CancellationToken ct = default);
    Task<ApiResponse<bool>> DeleteMemoryAsync(string id, CancellationToken ct = default);
    Task<ApiResponse<List<MemoryVectorDto>>> GetAllMemoriesAsync(CancellationToken ct = default);
    
    // === Réflexion autonome ===
    Task<ApiResponse<string>> ReflectAsync(CancellationToken ct = default);
    
    // === Profil utilisateur ===
    Task<ApiResponse<Dictionary<string, string>>> GetUserProfileAsync(CancellationToken ct = default);
    Task<ApiResponse<bool>> UpdateUserProfileAsync(string key, string value, CancellationToken ct = default);
}
