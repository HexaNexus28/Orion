using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Orion.Core.Configuration;
using Orion.Core.DTOs.Responses;
using Orion.Core.Interfaces.Services;

namespace Orion.Business.Services;

/// <summary>
/// EmbeddingService — génère des embeddings via Ollama nomic-embed-text
/// Utilisé par ConversationAgent pour la recherche RAG (pgvector)
/// </summary>
public class EmbeddingService : IEmbeddingService
{
    private readonly HttpClient _httpClient;
    private readonly ILogger<EmbeddingService> _logger;
    private readonly string _model;

    public EmbeddingService(HttpClient httpClient, IOptions<OllamaOptions> options, ILogger<EmbeddingService> logger)
    {
        _httpClient = httpClient;
        _logger = logger;
        _model = "nomic-embed-text";

        _httpClient.BaseAddress = new Uri(options.Value.BaseUrl);
        _httpClient.Timeout = TimeSpan.FromSeconds(30);
    }

    public async Task<ApiResponse<float[]>> GenerateEmbeddingAsync(string text, CancellationToken ct = default)
    {
        try
        {
            var request = new { model = _model, prompt = text };
            var response = await _httpClient.PostAsJsonAsync("/api/embeddings", request, ct);
            response.EnsureSuccessStatusCode();

            var json = await response.Content.ReadAsStringAsync(ct);
            var doc = JsonDocument.Parse(json);

            if (doc.RootElement.TryGetProperty("embedding", out var embeddingElement))
            {
                var embedding = embeddingElement.EnumerateArray()
                    .Select(e => (float)e.GetDouble())
                    .ToArray();

                _logger.LogDebug("[Embedding] Generated {Dims} dims for text ({Length} chars)",
                    embedding.Length, text.Length);
                return ApiResponse<float[]>.SuccessResponse(embedding);
            }

            _logger.LogWarning("[Embedding] No embedding in Ollama response");
            return ApiResponse<float[]>.ErrorResponse("No embedding in response");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[Embedding] Failed to generate embedding");
            return ApiResponse<float[]>.ErrorResponse($"Failed to generate embedding: {ex.Message}");
        }
    }
}
