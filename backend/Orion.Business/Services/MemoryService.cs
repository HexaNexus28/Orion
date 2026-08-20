using Microsoft.Extensions.Logging;
using Orion.Core.DTOs;
using Orion.Core.DTOs.Responses;
using Orion.Core.Entities;
using Orion.Core.Interfaces.Repositories;
using Orion.Core.Interfaces.Services;

namespace Orion.Business.Services;

public class MemoryService : IMemoryService
{
    private readonly IUnitOfWork _unitOfWork;
    private readonly IEmbeddingService _embeddingService;
    private readonly ILogger<MemoryService> _logger;

    public MemoryService(IUnitOfWork unitOfWork, IEmbeddingService embeddingService, ILogger<MemoryService> logger)
    {
        _unitOfWork = unitOfWork;
        _embeddingService = embeddingService;
        _logger = logger;
    }

    public async Task<ApiResponse<List<MemoryVectorDto>>> SearchSimilarAsync(
        string query, int topK = 5, CancellationToken ct = default)
    {
        _logger.LogInformation("Searching memories for: {Query}", query);

        var embeddingResponse = await _embeddingService.GenerateEmbeddingAsync(query, ct);
        if (!embeddingResponse.Success || embeddingResponse.Data == null)
        {
            _logger.LogWarning("Embedding generation failed for memory search");
            return ApiResponse<List<MemoryVectorDto>>.SuccessResponse(new List<MemoryVectorDto>());
        }

        var memories = await _unitOfWork.Memory.SearchSimilarAsync(embeddingResponse.Data, topK, ct);
        var dtos = memories.Select((m, i) => new MemoryVectorDto
        {
            Id = m.Id,
            Content = m.Content,
            Source = m.Source,
            Similarity = 1f - (i * 0.05f), // Approximate rank-based similarity
            CreatedAt = m.CreatedAt
        }).ToList();

        return ApiResponse<List<MemoryVectorDto>>.SuccessResponse(dtos);
    }

    public async Task<ApiResponse<bool>> SaveMemoryAsync(
        string content, string source, float importance = 1.0f, CancellationToken ct = default)
    {
        try
        {
            var embeddingResponse = await _embeddingService.GenerateEmbeddingAsync(content, ct);
            var embedding = embeddingResponse.Success && embeddingResponse.Data != null
                ? embeddingResponse.Data
                : Array.Empty<float>();

            if (embedding.Length == 0)
                _logger.LogWarning("[MemoryService] Embedding generation failed for content save, storing without vector");

            var memory = new MemoryVector
            {
                Id = Guid.NewGuid(),
                Content = content,
                Source = source,
                Importance = importance,
                Embedding = embedding,
                CreatedAt = DateTime.UtcNow
            };

            await _unitOfWork.Memory.AddAsync(memory, ct);
            await _unitOfWork.SaveChangesAsync(ct);

            // La colonne pgvector est hors du modele EF : sans cette ecriture explicite, le
            // souvenir est stocke SANS vecteur et reste invisible a la recherche semantique.
            if (embedding.Length > 0)
            {
                await _unitOfWork.Memory.SaveEmbeddingAsync(memory.Id, embedding, ct);
                _logger.LogInformation("[MemoryService] Souvenir {Id} enregistre ({Dims} dimensions)",
                    memory.Id, embedding.Length);
            }
            else
            {
                _logger.LogWarning("[MemoryService] Souvenir {Id} enregistre SANS vecteur — il ne " +
                    "remontera jamais dans une recherche", memory.Id);
            }

            return ApiResponse<bool>.SuccessResponse(true);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to save memory");
            return ApiResponse<bool>.ErrorResponse("Failed to save memory", 500);
        }
    }

    public async Task<ApiResponse<bool>> UpdateMemoryAsync(
        string id, string content, CancellationToken ct = default)
    {
        try
        {
            var memory = await _unitOfWork.Memory.GetByIdAsync(Guid.Parse(id), ct);
            if (memory == null)
            {
                return ApiResponse<bool>.NotFoundResponse("Memory not found");
            }

            memory.Content = content;
            memory.UpdatedAt = DateTime.UtcNow;
            _unitOfWork.Memory.Update(memory);
            await _unitOfWork.SaveChangesAsync(ct);

            _logger.LogInformation("Memory updated: {Id}", id);
            return ApiResponse<bool>.SuccessResponse(true);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to update memory: {Id}", id);
            return ApiResponse<bool>.ErrorResponse("Failed to update memory", 500);
        }
    }

    public async Task<ApiResponse<bool>> DeleteMemoryAsync(
        string id, CancellationToken ct = default)
    {
        try
        {
            var memory = await _unitOfWork.Memory.GetByIdAsync(Guid.Parse(id), ct);
            if (memory == null)
            {
                return ApiResponse<bool>.NotFoundResponse("Memory not found");
            }

            _unitOfWork.Memory.Remove(memory);
            await _unitOfWork.SaveChangesAsync(ct);

            _logger.LogInformation("Memory deleted: {Id}", id);
            return ApiResponse<bool>.SuccessResponse(true);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to delete memory: {Id}", id);
            return ApiResponse<bool>.ErrorResponse("Failed to delete memory", 500);
        }
    }

    public async Task<ApiResponse<List<MemoryVectorDto>>> GetAllMemoriesAsync(
        CancellationToken ct = default)
    {
        try
        {
            var memories = await _unitOfWork.Memory.GetAllAsync(ct);
            var dtos = memories.Select(m => new MemoryVectorDto
            {
                Id = m.Id,
                Content = m.Content,
                Source = m.Source,
                Similarity = m.Importance,
                CreatedAt = m.CreatedAt
            }).ToList();

            return ApiResponse<List<MemoryVectorDto>>.SuccessResponse(dtos);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to get all memories");
            return ApiResponse<List<MemoryVectorDto>>.ErrorResponse("Failed to get memories", 500);
        }
    }

    public async Task<ApiResponse<Dictionary<string, string>>> GetUserProfileAsync(CancellationToken ct = default)
    {
        var profiles = await _unitOfWork.UserProfile.GetAllAsync(ct);
        var dict = profiles.ToDictionary(p => p.Key, p => p.Value);
        
        return ApiResponse<Dictionary<string, string>>.SuccessResponse(dict);
    }

    public async Task<ApiResponse<bool>> UpdateUserProfileAsync(
        string key, string value, CancellationToken ct = default)
    {
        var existing = await _unitOfWork.UserProfile.GetByIdAsync(key, ct);
        
        if (existing == null)
        {
            await _unitOfWork.UserProfile.AddAsync(new UserProfile 
            { 
                Key = key, 
                Value = value,
                UpdatedAt = DateTime.UtcNow 
            }, ct);
        }
        else
        {
            existing.Value = value;
            existing.UpdatedAt = DateTime.UtcNow;
            _unitOfWork.UserProfile.Update(existing);
        }

        await _unitOfWork.SaveChangesAsync(ct);
        return ApiResponse<bool>.SuccessResponse(true);
    }
}
