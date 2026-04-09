using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Orion.Core.Configuration;
using Orion.Core.DTOs.Internal.LLM;
using Orion.Core.DTOs.Requests;
using Orion.Core.DTOs.Responses;
using Orion.Core.Enums;
using Orion.Core.Interfaces.LLM;

namespace Orion.Business.LLM;

public class AnthropicClient : ILLMClient
{
    private readonly HttpClient _httpClient;
    private readonly ILogger<AnthropicClient> _logger;
    private readonly AnthropicOptions _options;

    public AnthropicClient(HttpClient httpClient, IOptions<AnthropicOptions> options, ILogger<AnthropicClient> logger)
    {
        _httpClient = httpClient;
        _logger = logger;
        _options = options.Value;

        _httpClient.BaseAddress = new Uri("https://api.anthropic.com");
        _httpClient.DefaultRequestHeaders.Add("anthropic-version", "2023-06-01");
    }

    public LLMProvider Provider => LLMProvider.Anthropic;

    public bool IsAvailable() => !string.IsNullOrEmpty(_options.ApiKey);

    public async Task<ApiResponse<LLMResponse>> CompleteAsync(LLMRequest request, CancellationToken ct = default)
    {
        if (!IsAvailable())
        {
            return ApiResponse<LLMResponse>.ErrorResponse("Anthropic API key not configured", 503);
        }

        try
        {
            _httpClient.DefaultRequestHeaders.Authorization = 
                new AuthenticationHeaderValue("Bearer", _options.ApiKey);

            var anthropicRequest = new
            {
                model = request.Model ?? _options.Model,
                max_tokens = request.MaxTokens ?? _options.MaxTokens,
                temperature = request.Temperature ?? 0.7,
                system = request.SystemPrompt,
                messages = request.Messages.Select(m => new { role = m.Role, content = m.Content }).ToList()
            };

            var response = await _httpClient.PostAsJsonAsync("/v1/messages", anthropicRequest, ct);

            if (!response.IsSuccessStatusCode)
            {
                var error = await response.Content.ReadAsStringAsync(ct);
                _logger.LogWarning("Anthropic API error: {StatusCode} - {Error}", response.StatusCode, error);
                return ApiResponse<LLMResponse>.ErrorResponse($"Anthropic error: {error}", (int)response.StatusCode);
            }

            var jsonResponse = await response.Content.ReadFromJsonAsync<AnthropicResponse>(ct);

            return ApiResponse<LLMResponse>.SuccessResponse(new LLMResponse
            {
                Content = jsonResponse?.Content?.FirstOrDefault()?.Text ?? "",
                Provider = LLMProvider.Anthropic,
                Model = jsonResponse?.Model ?? anthropicRequest.model,
                TokensUsed = jsonResponse?.Usage?.OutputTokens + jsonResponse?.Usage?.InputTokens
            });
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Anthropic completion failed");
            return ApiResponse<LLMResponse>.ErrorResponse("Anthropic service unavailable", 503);
        }
    }

    public async Task StreamAsync(LLMRequest request, Func<string, Task> onChunk, CancellationToken ct = default)
    {
        if (!IsAvailable())
        {
            throw new InvalidOperationException("Anthropic API key not configured");
        }

        try
        {
            _httpClient.DefaultRequestHeaders.Authorization = 
                new AuthenticationHeaderValue("Bearer", _options.ApiKey);

            var anthropicRequest = new
            {
                model = request.Model ?? _options.Model,
                max_tokens = request.MaxTokens ?? _options.MaxTokens,
                temperature = request.Temperature ?? 0.7,
                system = request.SystemPrompt,
                messages = request.Messages.Select(m => new { role = m.Role, content = m.Content }).ToList(),
                stream = true
            };

            var response = await _httpClient.PostAsJsonAsync("/v1/messages", anthropicRequest, ct);
            response.EnsureSuccessStatusCode();

            using var stream = await response.Content.ReadAsStreamAsync(ct);
            using var reader = new StreamReader(stream);

            while (!reader.EndOfStream && !ct.IsCancellationRequested)
            {
                var line = await reader.ReadLineAsync(ct);
                if (string.IsNullOrWhiteSpace(line) || !line.StartsWith("data: ")) continue;

                var data = line.Substring(6);
                if (data == "[DONE]") break;

                try
                {
                    var chunk = JsonSerializer.Deserialize<AnthropicStreamChunk>(data);
                    if (chunk?.Delta?.Text != null)
                    {
                        await onChunk(chunk.Delta.Text);
                    }
                }
                catch (JsonException)
                {
                    // Ignore malformed chunks
                }
            }
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Anthropic streaming failed");
            throw;
        }
    }
}
