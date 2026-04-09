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
    private readonly ILogger<MemoryService> _logger;

    public MemoryService(IUnitOfWork unitOfWork, ILogger<MemoryService> logger)
    {
        _unitOfWork = unitOfWork;
        _logger = logger;
    }

    public async Task<ApiResponse<List<MemoryVectorDto>>> SearchSimilarAsync(
        string query, int topK = 5, CancellationToken ct = default)
    {
        // TODO: Generate embedding for query and search
        // For now, return empty list
        _logger.LogInformation("Searching memories for: {Query}", query);
        await Task.Yield(); // Suppress async warning
        return ApiResponse<List<MemoryVectorDto>>.SuccessResponse(new List<MemoryVectorDto>());
    }

    public async Task<ApiResponse<bool>> SaveMemoryAsync(
        string content, string source, float importance = 1.0f, CancellationToken ct = default)
    {
        try
        {
            var memory = new MemoryVector
            {
                Id = Guid.NewGuid(),
                Content = content,
                Source = source,
                Importance = importance,
                CreatedAt = DateTime.UtcNow
                // TODO: Generate embedding
            };

            await _unitOfWork.Memory.AddAsync(memory, ct);
            await _unitOfWork.SaveChangesAsync(ct);

            _logger.LogInformation("Memory saved: {Id}", memory.Id);
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

    public async Task<ApiResponse<string>> ReflectAsync(CancellationToken ct = default)
    {
        try
        {
            var memories = await _unitOfWork.Memory.GetAllAsync(ct);
            var recentMemories = memories
                .Where(m => m.CreatedAt > DateTime.UtcNow.AddDays(-7))
                .OrderByDescending(m => m.Importance)
                .Take(20)
                .ToList();

            if (!recentMemories.Any())
            {
                return ApiResponse<string>.SuccessResponse("Aucun souvenir récent à analyser.");
            }

            // Simple reflection summary (could be enhanced with LLM)
            var patterns = recentMemories
                .GroupBy(m => m.Source ?? "unknown")
                .Select(g => $"- {g.Key}: {g.Count()} souvenirs")
                .ToList();

            var summary = $"Synthèse hebdomadaire:\n" +
                $"Total souvenirs analysés: {recentMemories.Count}\n\n" +
                $"Répartition par source:\n{string.Join("\n", patterns)}\n\n" +
                $"Thèmes principaux: {string.Join(", ", recentMemories.Take(5).Select(m => m.Content.Substring(0, Math.Min(30, m.Content.Length)) + "..."))}";

            _logger.LogInformation("Memory reflection completed, analyzed {Count} memories", recentMemories.Count);
            return ApiResponse<string>.SuccessResponse(summary);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to generate memory reflection");
            return ApiResponse<string>.ErrorResponse("Failed to generate reflection", 500);
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
